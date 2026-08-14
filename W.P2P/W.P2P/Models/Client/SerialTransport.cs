using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using Avalonia.Threading;

namespace W.P2P.Models;

public class SerialTransport
{
    private readonly SerialPort _serialPort;
    private bool _isReading = false;
    private Thread _readingThread;
    public Action<byte[]> OnFrameReceived { get; set; }
    
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
    try
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            AppData.TerminalOutput.Add("ReadFrame started...");
        });
        
        byte[] startSyncArray = ReadExactBytes(1);
        if (startSyncArray == null)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                AppData.TerminalOutput.Add("ERROR: startSync timeout!");
            });
            return new byte[0];
        }
        
        byte startSync = startSyncArray[0];
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            AppData.TerminalOutput.Add($"Got start sync: {startSync:X2}");
        });
        
        if (startSync != 0xAA)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                AppData.TerminalOutput.Add($"ERROR: Invalid start sync! Got {startSync:X2}");
            });
            return new byte[0];
        }
        
        byte[] header = ReadExactBytes(111);
        if (header == null)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                AppData.TerminalOutput.Add("ERROR: header timeout!");
            });
            return new byte[0];
        }
        
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            AppData.TerminalOutput.Add("Got header (111 bytes)");
        });
        
        byte[] lengthBytes = ReadExactBytes(4);
        if (lengthBytes == null)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                AppData.TerminalOutput.Add("ERROR: lengthBytes timeout!");
            });
            return new byte[0];
        }
        
        int dataLen = BitConverter.ToInt32(lengthBytes, 0);
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            AppData.TerminalOutput.Add($"Got length: {dataLen} bytes");
        });
        
        if (dataLen < 0 || dataLen > 512)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                AppData.TerminalOutput.Add($"ERROR: Invalid dataLen: {dataLen}");
            });
            return new byte[0];
        }
        
        byte[] data = new byte[0];
        if (dataLen > 0)
        {
            data = ReadExactBytes(dataLen);
            if (data == null)
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AppData.TerminalOutput.Add("ERROR: data timeout!");
                });
                return new byte[0];
            }
            
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                AppData.TerminalOutput.Add($"Got data: {dataLen} bytes");
            });
        }
        
        byte[] frame = new byte[1 + 111 + 4 + dataLen];
        int pos = 0;
        frame[pos++] = 0xAA;
        Array.Copy(header, 0, frame, pos, 111);
        pos += 111;
        Array.Copy(lengthBytes, 0, frame, pos, 4);
        pos += 4;
        if (dataLen > 0)
        {
            Array.Copy(data, 0, frame, pos, dataLen);
        }
        
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            AppData.TerminalOutput.Add($"Frame complete: {frame.Length} bytes total");
        });
        
        return frame;
    }
    catch (Exception ex)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            AppData.TerminalOutput.Add($"ERROR in ReadFrame: {ex.Message} | {ex.StackTrace}");
        });
        return new byte[0];
    }
}

private byte[] ReadExactBytes(int count)
{
    byte[] buffer = new byte[count];
    int totalRead = 0;

    while (totalRead < count)
    {
        try
        {
            int bytesRead = _serialPort.Read(buffer, totalRead, count - totalRead);
            
            if (bytesRead == 0)
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AppData.TerminalOutput.Add($"ReadExactBytes: got 0 bytes while reading {count}!");
                });
                return null;
            }

            totalRead += bytesRead;
        }
        catch (TimeoutException)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                AppData.TerminalOutput.Add($"ReadExactBytes: timeout! wanted {count}, got {totalRead}");
            });
            return null;
        }
    }

    return buffer;
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
                if (_serialPort.IsOpen)  // ← NUR IsOpen check, NICHT BytesToRead!
                {
                    var frame = ReceiveFrame();  // ← ReceiveFrame wartet selbst auf Bytes
                
                    if (frame.Length > 0)
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            AppData.TerminalOutput.Add($"Received frame from UNO: {frame.Length} bytes");
                        });
                    
                        OnFrameReceived?.Invoke(frame);
                    }
                }
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
}
