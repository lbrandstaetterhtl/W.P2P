using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using Avalonia.Threading;

namespace W.P2P.Models;

public class SerialTransport
{
    private const int HEADER_SIZE = 145;
    private readonly SerialPort _serialPort;
    private bool _isReading = false;
    private Thread _readingThread;
    public DataModels.ArduinoConfig ArduinoConfig { get; set; }
    public event Action<byte[]> OnFrameReceived;
    
    public SerialTransport(string portName, int baudRate)
    {
        _serialPort = new SerialPort(portName, baudRate);
    }
    
    public void Connect()
    {
        try
        {
            if (!_serialPort.IsOpen)
            {
                _serialPort.Open();

                Thread.Sleep(2000);
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                AppData.TerminalOutput.Add(
                    $"Serial port opened: {_serialPort.PortName} at {_serialPort.BaudRate} baud.");
            }
            else
            {
                AppData.TerminalOutput.Add($"Serial port {_serialPort.PortName} is already open.");
            }
        }
        catch (Exception ex)
        {
            AppData.TerminalOutput.Add($"Error opening serial port: {ex.Message}");
        }
    }
    
    public void SendFrame(byte[] frame)
    {
        try
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Write(frame, 0, frame.Length);

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AppData.TerminalOutput.Add($"Sent frame to UNO: {frame.Length} bytes");
                });
            }
            else
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                { 
                    AppData.TerminalOutput.Add($"Serial port {_serialPort.PortName} is not open. Cannot send frame.");
                });
            }
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                AppData.TerminalOutput.Add($"Error sending frame: {ex.Message}");
            });
        }
    }
    
    public byte[] ReceiveFrame()
    {
        List<byte> frame = new List<byte>();
    
        var sync = ReadBytes(1);
        frame.AddRange(sync);
    
        var header = ReadBytes(HEADER_SIZE);
        frame.AddRange(header);
        var lengthArray = ReadBytes(1);
        var dataLength = lengthArray[0];
        frame.AddRange(lengthArray);
    
        if (dataLength > 0)
        {
            var data = ReadBytes(dataLength);
            frame.AddRange(data);
        }
    
        var footer = ReadBytes(2);
        frame.AddRange(footer);
    
        return frame.ToArray();
    }

    public byte[] ReadBytes(int count)
    {
        try
        {
            byte[] buffer = new byte[count];
            int totalRead = 0;

            while (totalRead < count)
            {
                if (_serialPort.BytesToRead > 0)
                {
                    int available = _serialPort.BytesToRead;
                    int toRead = Math.Min(available, count - totalRead);
                    int bytesRead = _serialPort.Read(buffer, totalRead, toRead);
                    totalRead += bytesRead;
                }
                else
                {
                    Thread.Sleep(10);
                }
            }

            return buffer;
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                AppData.TerminalOutput.Add($"Error in reading frame: {ex.Message}");
            });
        
            return [];
        }
    }
    
    public void StartReading()
    {
        if (_isReading) return;
        _isReading = true;
        _readingThread = new Thread(ReadThread);
        _readingThread.Start();
    }

    private void ReadThread()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            AppData.TerminalOutput.Add("ReadThread is now running!");
        });

        try
        {
            while (_isReading)
            {
                if (_serialPort.IsOpen && _serialPort.BytesToRead > 0)
                {
                    var frame = ReceiveFrame();

                    if (frame.Length > 0)
                    {
                        OnFrameReceived?.Invoke(frame);
                    }
                }
            
                Thread.Sleep(50);
            }
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                AppData.TerminalOutput.Add($"Error in reading thread: {ex.Message}");
            });
        }
    }
    
    public void SendConfig()
    {
        if (_serialPort.IsOpen)
        {
            List<byte> toSend = new();
            
            toSend.Add(0xFF);
            toSend.AddRange(ArduinoConfig.TargetId);
            toSend.AddRange(ArduinoConfig.MyId);
            
            _serialPort.Write(toSend.ToArray(), 0, toSend.Count);
        }
    }
}
