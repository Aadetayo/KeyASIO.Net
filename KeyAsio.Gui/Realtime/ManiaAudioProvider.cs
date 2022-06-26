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

public class ManiaAudioProvider : IAudioProvider
{
    private static readonly ILogger Logger = SharedUtils.GetLogger(nameof(ManiaAudioProvider));
    private readonly RealtimeModeManager _realtimeModeManager;

    private List<Queue<PlayableNode>> _hitQueue = new();
    private PlayableNode?[] _hitQueueCache = Array.Empty<PlayableNode>();

    private Queue<PlayableNode> _playQueue = new();
    private Queue<PlayableNode> _autoPlayQueue = new();

    private PlayableNode? _firstAutoNode;
    private PlayableNode? _firstPlayNode;

    public ManiaAudioProvider(RealtimeModeManager realtimeModeManager)
    {
        _realtimeModeManager = realtimeModeManager;
    }

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

        var first = includeKey ? _firstPlayNode : _firstAutoNode;
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
        
        var queue = _hitQueue[keyIndex];
        while (true)
        {
            if (queue.TryPeek(out var node))
            {
                if (playTime < node.Offset - 80 /*odMax*/)
                {
                    _hitQueueCache[keyIndex] = null;
                    break;
                }

                if (playTime <= node.Offset + 50 /*odMax*/)
                {
                    _hitQueueCache[keyIndex] = queue.Dequeue();
                    Logger.LogDebug("Dequeued and will use Col." + keyIndex);
                    break;
                }

                queue.Dequeue();
                Logger.LogDebug("Dropped Col." + keyIndex);
                _hitQueueCache[keyIndex] = null;
            }
            else
            {
                _hitQueueCache[keyIndex] = null;
                break;
            }
        }

        var playableNode = _hitQueueCache[keyIndex];
        if (playableNode == null)
        {
            _hitQueue[keyIndex].TryPeek(out playableNode);
            Logger.LogDebug("Use first");
        }
        else
        {
            Logger.LogDebug("Use cache");
        }

        if (playableNode != null && _realtimeModeManager.TryGetAudioByNode(playableNode, out var cachedSound))
        {
            return new[] { new PlaybackInfo(cachedSound, playableNode.Volume, playableNode.Balance) };
        }

        return ReturnDefaultAndLog("No audio returns.");
    }

    public void FillAudioList(IReadOnlyList<HitsoundNode> nodeList, List<PlayableNode> keyList, List<PlayableNode> playbackList)
    {
        foreach (var hitsoundNode in nodeList)
        {
            if (hitsoundNode is not PlayableNode playableNode) continue;

            if (playableNode.PlayablePriority is PlayablePriority.Sampling)
            {
                if (!AppSettings.RealtimeOptions.IgnoreStoryboardSamples)
                {
                    playbackList.Add(playableNode);
                }
            }
            else
            {
                keyList.Add(playableNode);
            }
        }
    }

    public void ResetNodes(int playTime)
    {
        _hitQueue = GetHitQueue(_realtimeModeManager.KeyList, playTime);
        _hitQueueCache = new PlayableNode[_hitQueue.Count];

        _autoPlayQueue = new Queue<PlayableNode>(_realtimeModeManager.KeyList);
        _playQueue = new Queue<PlayableNode>(_realtimeModeManager.PlaybackList.Where(k => k.Offset >= playTime));
        _autoPlayQueue.TryDequeue(out _firstAutoNode);
        _playQueue.TryDequeue(out _firstPlayNode);
    }

    private List<Queue<PlayableNode>> GetHitQueue(IReadOnlyList<PlayableNode> keyList, int playTime)
    {
        if (_realtimeModeManager.OsuFile == null)
            return new List<Queue<PlayableNode>>();

        var keyCount = (int)_realtimeModeManager.OsuFile.Difficulty.CircleSize;
        var list = new List<Queue<PlayableNode>>(keyCount);
        for (int i = 0; i < keyCount; i++)
        {
            list.Add(new Queue<PlayableNode>());
        }

        foreach (var playableNode in keyList.Where(k => k.Offset >= playTime))
        {
            var ratio = (playableNode.Balance + 1d) / 2;
            var column = (int)Math.Round(ratio * keyCount - 0.5);
            list[column].Enqueue(playableNode);
        }

        return list;
    }

    private IEnumerable<PlaybackInfo> GetNextPlaybackAudio(PlayableNode? firstNode, int playTime, bool includeKey)
    {
        while (firstNode != null)
        {
            if (playTime < firstNode.Offset)
            {
                break;
            }

            if (_realtimeModeManager.TryGetAudioByNode(firstNode, out var cachedSound))
            {
                yield return new PlaybackInfo(cachedSound, firstNode.Volume, firstNode.Balance);
            }

            if (includeKey)
            {
                _playQueue.TryDequeue(out firstNode);
            }
            else
            {
                _autoPlayQueue.TryDequeue(out firstNode);
            }
        }

        if (includeKey)
        {
            _firstPlayNode = firstNode;
        }
        else
        {
            _firstAutoNode = firstNode;
        }
    }

    private static IEnumerable<PlaybackInfo> ReturnDefaultAndLog(string message, LogLevel logLevel = LogLevel.Debug)
    {
        Logger.Log(logLevel, message);
        return Array.Empty<PlaybackInfo>();
    }
}