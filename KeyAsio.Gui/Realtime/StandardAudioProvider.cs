﻿using System;
using System.Collections.Generic;
using System.Linq;
using Coosu.Beatmap.Extensions.Playback;
using KeyAsio.Gui.Configuration;
using KeyAsio.Gui.Models;
using KeyAsio.Gui.Utils;
using Microsoft.Extensions.Logging;
using Milki.Extensions.MixPlayer.NAudioExtensions;

namespace KeyAsio.Gui.Realtime;

public class StandardAudioProvider : IAudioProvider
{
    private static readonly ILogger Logger = SharedUtils.GetLogger(nameof(StandardAudioProvider));
    private readonly RealtimeModeManager _realtimeModeManager;

    private Queue<PlayableNode> _hitQueue = new();
    private Queue<HitsoundNode> _playQueue = new();

    private PlayableNode? _firstNode;
    private HitsoundNode? _firstPlayNode;

    public StandardAudioProvider(RealtimeModeManager realtimeModeManager)
    {
        _realtimeModeManager = realtimeModeManager;
    }

    public int KeyThresholdMilliseconds { get; set; } = 100;
    public bool IsStarted => _realtimeModeManager.IsStarted;
    public int PlayTime => _realtimeModeManager.PlayTime;
    public AudioPlaybackEngine? AudioPlaybackEngine => SharedViewModel.Instance.AudioPlaybackEngine;
    public AppSettings AppSettings => ConfigurationFactory.GetConfiguration<AppSettings>();

    public IEnumerable<PlaybackInfo> GetPlaybackAudio(bool includeKey)
    {
        var playTime = PlayTime;

        var audioPlaybackEngine = SharedViewModel.Instance.AudioPlaybackEngine;
        if (audioPlaybackEngine == null)
        {
            return Array.Empty<PlaybackInfo>();
        }

        if (!IsStarted)
        {
            return Array.Empty<PlaybackInfo>();
        }

        var first = includeKey ? _firstPlayNode : _firstNode;
        if (first == null)
        {
            return Array.Empty<PlaybackInfo>();
        }

        if (playTime < first.Offset)
        {
            return Array.Empty<PlaybackInfo>();
        }

        return GetNextPlaybackAudio(first, playTime, includeKey);
    }

    public IEnumerable<PlaybackInfo> GetKeyAudio(int keyIndex, int keyTotal)
    {
        using var _ = DebugUtils.CreateTimer($"GetSoundOnClick", Logger);

        var playTime = PlayTime;
        var audioPlaybackEngine = AudioPlaybackEngine;
        var isStarted = IsStarted;

        if (audioPlaybackEngine == null) return ReturnDefaultAndLog("Engine not ready, return empty.", LogLevel.Warning);
        if (!isStarted) return ReturnDefaultAndLog("Game hasn't started, return empty.");

        var first = _firstNode;
        if (first == null) return ReturnDefaultAndLog("First is null, no item returns.");

        Logger.LogDebug($"Click: {playTime}; First node: {first.Offset}");

        if (playTime < first.Offset - KeyThresholdMilliseconds) return ReturnDefaultAndLog("Haven't reached first, no item returns.");

        if (playTime < first.Offset + KeyThresholdMilliseconds) // click soon~0~late
        {
            return GetNextKeyAudio(first, playTime, false);
        }

        return GetNextKeyAudio(first, playTime, true);
    }

    public void FillAudioList(IReadOnlyList<HitsoundNode> nodeList, List<PlayableNode> keyList, List<HitsoundNode> playbackList)
    {
        var secondaryCache = new List<PlayableNode>();
        foreach (var hitsoundNode in nodeList)
        {
            if (hitsoundNode is not PlayableNode playableNode)
            {
                var controlNode = (ControlNode)hitsoundNode;
                if (controlNode.ControlType is ControlType.ChangeBalance or ControlType.None) continue;
                //controlNode.Balance = 1;
                playbackList.Add(controlNode);
                continue;
            }

            if (playableNode.PlayablePriority == PlayablePriority.Primary)
            {
                CheckSecondary();
                secondaryCache.Clear();
                keyList.Add(playableNode);
            }
            else if (playableNode.PlayablePriority is PlayablePriority.Secondary)
            {
                var sliderTailBehavior = AppSettings.RealtimeOptions.SliderTailPlaybackBehavior;
                if (sliderTailBehavior == SliderTailPlaybackBehavior.Normal)
                {
                    playbackList.Add(playableNode);
                }
                else if (sliderTailBehavior == SliderTailPlaybackBehavior.KeepReverse)
                {
                    secondaryCache.Add(playableNode);
                }
            }
            else if (playableNode.PlayablePriority is PlayablePriority.Effects)
            {
                if (!AppSettings.RealtimeOptions.IgnoreSliderTicksAndSlides)
                {
                    playbackList.Add(playableNode);
                }
            }
            else if (playableNode.PlayablePriority is PlayablePriority.Sampling)
            {
                if (!AppSettings.RealtimeOptions.IgnoreStoryboardSamples)
                {
                    playbackList.Add(playableNode);
                }
            }
        }

        CheckSecondary();

        void CheckSecondary()
        {
            if (secondaryCache.Count <= 1) return;
            playbackList.AddRange(secondaryCache);
        }
    }

    public void ResetNodes(int playTime)
    {
        _hitQueue = new Queue<PlayableNode>(_realtimeModeManager.KeyList);
        _playQueue = new Queue<HitsoundNode>(_realtimeModeManager.PlaybackList.Where(k => k.Offset >= PlayTime));
        _hitQueue.TryDequeue(out _firstNode);
        _playQueue.TryDequeue(out _firstPlayNode);
    }

    private IEnumerable<PlaybackInfo> GetNextKeyAudio(PlayableNode? firstNode, int playTime, bool checkPreTiming)
    {
        int counter = 0;
        bool isFirst = true;
        PlayableNode? preNode = null;
        while (firstNode != null)
        {
            if (preNode?.Guid != firstNode.Guid)
            {
                if (!isFirst && !checkPreTiming && playTime < firstNode.Offset - 3)
                {
                    //Logger.LogWarning($"Haven't reached first, return empty.");
                    break;
                }

                if (checkPreTiming && playTime >= firstNode.Offset + KeyThresholdMilliseconds)
                {
                    _hitQueue.TryDequeue(out firstNode);
                    continue;
                }

            }

            isFirst = false;
            checkPreTiming = false;
            if (_realtimeModeManager.TryGetAudioByNode(firstNode, out var cachedSound))
            {
                counter++;
                preNode = firstNode;
                yield return new PlaybackInfo(cachedSound, firstNode);
            }

            _hitQueue.TryDequeue(out firstNode);
        }

        _firstNode = firstNode;
        if (counter == 0)
        {
            Logger.LogWarning($"Counter is zero, no item returns.");
        }
    }

    private IEnumerable<PlaybackInfo> GetNextPlaybackAudio(HitsoundNode? firstNode, int playTime, bool includeKey)
    {
        while (firstNode != null)
        {
            if (playTime < firstNode.Offset)
            {
                break;
            }

            if (_realtimeModeManager.TryGetAudioByNode(firstNode, out var cachedSound))
            {
                yield return new PlaybackInfo(cachedSound, firstNode);
            }

            if (includeKey)
            {
                _playQueue.TryDequeue(out firstNode);
            }
            else
            {
                _hitQueue.TryDequeue(out var node);
                firstNode = node;
            }
        }

        if (includeKey)
        {
            _firstPlayNode = firstNode;
        }
        else
        {
            _firstNode = (PlayableNode?)firstNode;
        }
    }

    private static IEnumerable<PlaybackInfo> ReturnDefaultAndLog(string message, LogLevel logLevel = LogLevel.Debug)
    {
        Logger.Log(logLevel, message);
        return Array.Empty<PlaybackInfo>();
    }
}