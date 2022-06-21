﻿using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Milki.Extensions.MixPlayer.Devices;
using Milki.Extensions.MixPlayer.NAudioExtensions;
using Milki.Extensions.MixPlayer.NAudioExtensions.Wave;
using Milki.Extensions.MouseKeyHook;
using NAudio.Wave;

namespace KeyAsio.Net;

public partial class FormTrigger : Form
{
    private static readonly UnicodeRange[] ChineseRange =
    {
        UnicodeRanges.BasicLatin, UnicodeRange.Create((char)0x4E00, (char)0x9FA5)
    };
    private static readonly JsonStringEnumConverter JsonStringEnumConverter = new();

    private readonly AppSettings _settings;

    private readonly IKeyboardHook _keyboardHook;

    private readonly DeviceDescription? _deviceDescription;
    private IWavePlayer? _device;
    private AudioPlaybackEngine? _engine;

    public FormTrigger(DeviceDescription? deviceDescription, AppSettings settings)
    {
        InitializeComponent();
        _keyboardHook = KeyboardHookFactory.CreateGlobal();
        _deviceDescription = deviceDescription;
        _settings = settings;

        Load += Form1_Load;
        Closed += Form1_Closed;
    }

    private async void Form1_Load(object? sender, EventArgs e)
    {
        if (!CreateDevice(true)) return;
        Console.WriteLine();

        if (_device is AsioOut)
        {
            btnAsio.Enabled = true;
        }

        var waveFormat = new WaveFormat(_settings.SampleRate, _settings.Bits, _settings.Channels);
        var cacheSound = await CachedSoundFactory.GetOrCreateCacheSound(waveFormat, _settings.HitsoundPath);
        foreach (var key in _settings.Keys)
        {
            RegisterHotKey(key, cacheSound);
        }

        Console.WriteLine("Your active keys: " + string.Join(",", _settings.Keys.OrderBy(k => k)));
        Console.WriteLine("Initialization done.");
    }

    private void Form1_Closed(object? sender, EventArgs e)
    {
        _keyboardHook.Dispose();

        _device?.Stop();
        _device?.Dispose();
    }

    private bool CreateDevice(bool printCommonMessage)
    {
        DeviceDescription? actualDeviceInfo;
        try
        {
            _device = DeviceCreationHelper.CreateDevice(out actualDeviceInfo, _deviceDescription);
            if (_device is AsioOut ao)
            {
                ao.DriverResetRequest += Ao_DriverResetRequest;
            }
            _engine = new AudioPlaybackEngine(_device, _settings.SampleRate, _settings.Channels,
                notifyProgress: false, enableVolume: false)
            {
                Volume = 0.1f
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error occurs while initializing device: {ex.GetType().FullName}: {ex.Message}\r\n{ex.StackTrace}");
            Close();
            return false;
        }

        if (printCommonMessage)
        {
            Console.WriteLine("Active device information: ");
            var aymInfo = new
            {
                DeviceInfo = actualDeviceInfo,
                WaveFormat = _engine?.WaveFormat
            };
            Console.WriteLine(JsonSerializer.Serialize(aymInfo, new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(ChineseRange),
                WriteIndented = true,
                Converters = { JsonStringEnumConverter }
            }));
        }

        if (_device is AsioOut asioOut)
        {
            Console.WriteLine("ASIO information: ");
            var aymInfo = new
            {
                FramesPerBuffer = asioOut.FramesPerBuffer,
                PlaybackLatency = asioOut.PlaybackLatency,
            };
            Console.WriteLine(JsonSerializer.Serialize(aymInfo, new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(ChineseRange),
                WriteIndented = true,
                Converters = { JsonStringEnumConverter }
            }));
        }

        return true;
    }

    private void RegisterHotKey(HookKeys key, CachedSound? cacheSound)
    {
        _keyboardHook.RegisterKey(key, (_, hookKey, action) =>
        {
            if (action == KeyAction.KeyDown)
            {
                _engine?.PlaySound(cacheSound);
                if (_settings.Debugging)
                {
                    Console.WriteLine($"{hookKey} {action}");
                }
            }
            else
            {
                if (_settings.Debugging)
                {
                    Console.WriteLine($"{hookKey} {action}");
                }
            }
        });
    }

    private void btnAsio_Click(object sender, EventArgs e)
    {
        if (_device is AsioOut asioOut)
        {
            asioOut.ShowControlPanel();
        }
    }

    private void Ao_DriverResetRequest(object? sender, EventArgs e)
    {
        _engine?.Dispose();
        Console.WriteLine("Driver requested to reset.");
        var success = CreateDevice(false);
        if (success)
        {
            Console.WriteLine("Driver reseted.\r\n");
        }
        else
        {
            Console.WriteLine("Driver failed to reset.\r\n");
        }
    }
}