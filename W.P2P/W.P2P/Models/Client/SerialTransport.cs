using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading;
using Avalonia.Threading;

namespace W.P2P.Models;

public class SerialTransport
{
    private const int HEADER_SIZE = 145;
    private const byte SYNC_BYTE = 0xAA;
    private const byte LOG_BYTE = 0xEE;
    private const int IdleTimeoutMs = 1000;

    private readonly SerialPort _serialPort;
    private bool _isReading;
    private Thread _readingThread;

    public DataModels.ArduinoConfig ArduinoConfig { get; set; }
    public event Action<byte[]> OnFrameReceived;

    public SerialTransport(string portName, int baudRate)
    {
        _serialPort = new SerialPort(portName, baudRate);
    }

    private static void Log(string message) =>
        Dispatcher.UIThread.Post(() => AppData.TerminalOutput.Add(message));

    public void Connect()
    {
        try
        {
            if (_serialPort.IsOpen)
            {
                Log($"Serial port {_serialPort.PortName} is already open.");
                return;
            }
            
            _serialPort.Open();

            // Uno resettet beim Oeffnen (DTR/RTS-Puls). ~2s Bootloader abwarten,
            // sonst frisst der Bootloader die Config. Blockiert den Thread 2s.
            Thread.Sleep(2000);

            Log($"Serial port opened: {_serialPort.PortName} at {_serialPort.BaudRate} baud.");
        }
        catch (Exception ex)
        {
            Log($"Error opening serial port: {ex.Message}");
        }
    }

    public void SendFrame(byte[] frame)
    {
        try
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Write(frame, 0, frame.Length);
                Log($"Sent frame to UNO: {frame.Length} bytes");
            }
            else
            {
                Log($"Serial port {_serialPort.PortName} is not open. Cannot send frame.");
            }
        }
        catch (Exception ex)
        {
            Log($"Error sending frame: {ex.Message}");
        }
    }

    // Sync wurde bereits vom ReadThread konsumiert -> hier voranstellen
    private byte[] ReceiveFrameAfterSync()
    {
        List<byte> frame = new() { SYNC_BYTE };

        var header = ReadBytes(HEADER_SIZE);
        if (header.Length < HEADER_SIZE) return [];
        frame.AddRange(header);

        var lengthArray = ReadBytes(1);
        if (lengthArray.Length == 0) return [];
        byte dataLength = lengthArray[0];
        frame.Add(dataLength);

        if (dataLength > 0)
        {
            var data = ReadBytes(dataLength);
            if (data.Length < dataLength) return [];
            frame.AddRange(data);
        }

        var footer = ReadBytes(2);
        if (footer.Length < 2) return [];
        frame.AddRange(footer);

        return frame.ToArray();
    }

    public byte[] ReadBytes(int count)
    {
        try
        {
            var buffer = new byte[count];
            int totalRead = 0;
            var idle = Stopwatch.StartNew();

            while (totalRead < count)
            {
                if (_serialPort.BytesToRead > 0)
                {
                    totalRead += _serialPort.Read(buffer, totalRead, count - totalRead);
                    idle.Restart();
                }
                else if (idle.ElapsedMilliseconds > IdleTimeoutMs)
                {
                    return [];   // Timeout -> leer, Aufrufer behandelt das
                }
                else
                {
                    Thread.Sleep(2);
                }
            }

            return buffer;
        }
        catch (Exception ex)
        {
            Log($"Error in reading frame: {ex.Message}");
            return [];
        }
    }

    public void StartReading()
    {
        if (_isReading) return;
        _isReading = true;
        _readingThread = new Thread(ReadThread) { IsBackground = true };
        _readingThread.Start();
    }

    public void StopReading()
    {
        _isReading = false;
        _readingThread?.Join(500);
    }

    private void ReadThread()
    {
        Log("ReadThread is now running!");

        try
        {
            while (_isReading)
            {
                if (_serialPort.IsOpen && _serialPort.BytesToRead > 0)
                {
                    var first = ReadBytes(1);
                    if (first.Length == 0) { Thread.Sleep(20); continue; }

                    if (first[0] == SYNC_BYTE)
                    {
                        var frame = ReceiveFrameAfterSync();
                        if (frame.Length > 0)
                            OnFrameReceived?.Invoke(frame);
                    }
                    else if (first[0] == LOG_BYTE)
                    {
                        ReadLogMessage();
                    }
                    // alles andere: ignorieren, naechstes Byte pruefen (Resync)
                }

                Thread.Sleep(20);
            }
        }
        catch (Exception ex)
        {
            Log($"Error in reading thread: {ex.Message}");
        }
    }

    private void ReadLogMessage()
    {
        var lenArr = ReadBytes(1);
        if (lenArr.Length == 0) return;
        int len = lenArr[0];

        if (len == 0)
        {
            Log("[UNO] ");
            return;
        }

        var msg = ReadBytes(len);
        if (msg.Length < len) return;

        var text = Encoding.ASCII.GetString(msg);
        Log($"[UNO] {text}");
    }

    public void SendConfig()
    {
        if (!_serialPort.IsOpen) return;

        List<byte> toSend = new();
        toSend.Add(0xFF);
        toSend.AddRange(ArduinoConfig.TargetId);   // 5 Byte
        toSend.AddRange(ArduinoConfig.MyId);       // 5 Byte

        _serialPort.Write(toSend.ToArray(), 0, toSend.Count);
    }
}