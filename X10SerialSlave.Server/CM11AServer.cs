using HA4IoT.Hardware.X10;
using HA4IoT.Hardware.X10.Codes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;

namespace X10SerialSlave.Server
{
    /// <summary>
    /// Enables control of the X10 CM11A device.
    /// </summary>
    public sealed class CM11AServer : IX10Controller, IDisposable
    {
        #region Static members
        static readonly ConcurrentDictionary<string, CM11AServer> Instances;

        static CM11AServer()
        {
            Instances = new ConcurrentDictionary<string, CM11AServer>();
        }

        public static CM11AServer GetInstance()
        {
            string aqs = SerialDevice.GetDeviceSelector();
            DeviceInformationCollection dic = DeviceInformation.FindAllAsync(aqs).AsTask().Result;
            DeviceInformation di = dic.FirstOrDefault();
            return GetInstance(di.Id);
        }

        /// <summary>
        /// Gets an instance the CM11A device and opens the specified port for communication.
        /// </summary>
        /// <param name="portName">The name of the port to open, such as COM1.</param>
        public static CM11AServer GetInstance(string portName)
        {
            if (string.IsNullOrEmpty(portName))
                throw new ArgumentNullException(nameof(portName));

            CM11AServer instance = null;
            lock (Instances)
            {
                if (Instances.ContainsKey(portName))
                {
                    instance = Instances[portName];
                }
                else
                {
                    try
                    {
                        instance = new CM11AServer(portName);
                    }
                    catch (Exception ex)
                    {
                        instance = null;
                        throw new IOException(string.Format("There was an error creating the CM11A device on {0}.", portName), ex);
                    }
                    finally
                    {
                        if (instance != null)
                        {
                            Interlocked.Increment(ref instance._refCount);
                            Instances.AddOrUpdate(portName, instance, (key, existingVal) => instance);
                        }
                    }
                }
            }

            return instance;
        }
        #endregion

        #region Instance members
        private readonly X10HouseCode _houseCodeToMonitor;
        private readonly CancellationTokenSource _readCancellationTokenSource;
        private SerialDevice _serialDevice;
        private DataWriter _dataWriteObject;
        private DataReader _dataReaderObject;
        private int _refCount;
        private readonly object _syncRoot;

        public string PortName { get; private set; }

        /// <summary>
        /// Creates the CM11A device and opens the specified port for communication.
        /// </summary>
        /// <param name="portName">The name of the port to open, such as "COM1".</param>
        private CM11AServer(string portName)
        {
            _syncRoot = new object();
            _refCount = 0;
            _houseCodeToMonitor = X10HouseCodes.A;
            _readCancellationTokenSource = new CancellationTokenSource();

            PortName = portName;
        }

        public void Initialize()
        {
            try
            {
                _serialDevice = SerialDevice.FromIdAsync(PortName).GetResults();
                _serialDevice.WriteTimeout = TimeSpan.FromMilliseconds(2500);
                _serialDevice.ReadTimeout = TimeSpan.FromMilliseconds(2500);
                _serialDevice.BaudRate = 4800;
                _serialDevice.StopBits = SerialStopBitCount.One;
                _serialDevice.DataBits = 8;
                _serialDevice.Parity = SerialParity.None;
                _serialDevice.Handshake = SerialHandshake.None;
                _serialDevice.PinChanged += _serialDevice_PinChanged;
            }
            catch (Exception ex)
            {
                Dispose();
                throw new IOException(string.Format("There was an error creating the CM11A device on {0}.", PortName), ex);
            }
        }

        private void _serialDevice_PinChanged(SerialDevice sender, PinChangedEventArgs args)
        {
            if (args.PinChange != SerialPinChange.RingIndicator) return;
            _dataReaderObject = new DataReader(_serialDevice.InputStream)
            {
                InputStreamOptions = InputStreamOptions.Partial
            };
            byte response = _dataReaderObject.ReadByte();
            _dataReaderObject.DetachStream();
            _dataReaderObject = null;

            HandleResponse(response);
        }

        /// <summary>
        /// Sends a command to the CM11A device for a specific house code and unit code.
        /// </summary>
        /// <param name="houseCode">The house code of the unit to control.</param>
        /// <param name="unitCode">The number (1-16) of the unit to control.</param>
        /// <param name="command">The command to send to the specified unit.</param>
        public void SendCommand(byte houseCode, byte unitCode, byte command)
        {
            SendCommand(houseCode, unitCode, command, Constants.DefaultDimBrightAmount);
        }

        /// <summary>
        /// Sends a command to the CM11A device for a specific house code and unit code.
        /// </summary>
        /// <param name="houseCode">The house code of the unit to control.</param>
        /// <param name="unitCode">The number (1-16) of the unit to control.</param>
        /// <param name="command">The command to send to the specified unit.</param>
        /// <param name="dimOrBrightAmount">The percentage (1-100) the specified unit will be dimmed or brightened.</param>
        public void SendCommand(byte houseCode, byte unitCode, byte command, int dimOrBrightAmount)
        {
            if (unitCode < 1 || unitCode > 16)
                throw new ArgumentOutOfRangeException(nameof(unitCode), unitCode, "The unit code must be between 1 - 16.");

            if (dimOrBrightAmount < 1 || dimOrBrightAmount > 100)
                throw new ArgumentOutOfRangeException(nameof(dimOrBrightAmount), dimOrBrightAmount, "dimOrBrightAmount must be between 1 - 100");

            byte[] address = CreateAddress(houseCode, unitCode);
            byte[] function = CreateFunction(houseCode, command, dimOrBrightAmount);

            bool success = false;
            int numAttempts = 0;

            lock (_syncRoot)
            {
                do
                {
                    numAttempts++;
                    if (numAttempts > 10)
                        throw new IOException("The CM11A device was unable to send the requested address.");
                    try
                    {
                        if (SendCommandBytes(address))
                            success = SendCommandBytes(function);
                    }
                    catch { }

                } while (!success);
            }
        }

        /// <summary>
        /// Disposes of the CM11A object and closes the serial port.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Decrement(ref _refCount) != 0) return;
            lock (Instances)
            {
                CM11AServer cm11A;
                Instances.TryRemove(PortName, out cm11A);
            }
            _serialDevice?.Dispose();
            _serialDevice = null;
        }

        private bool SendCommandBytes(byte[] command)
        {
            byte checksum = CreateChecksum(command);
            WriteBytes(command);
            byte[] response = GetBytes();
            bool success = (checksum == response[0]);
            if (!success)
                HandleResponse(response[0]);
            else
            {
                WriteBytes(new byte[] { X10DeviceCommands.AcknowledgeCode });
                GetBytes();
            }

            return success;
        }

        private byte CreateChecksum(byte[] command)
        {
            return Convert.ToByte((command[0] + command[1]) & 0xff);
        }

        private byte[] CreateAddress(byte houseCode, byte unitCode)
        {
            byte address = Convert.ToByte((houseCode << 4) | unitCode);
            return new byte[] { X10DeviceCommands.AddressNotificationCode, address };
        }

        private byte[] CreateFunction(byte houseCode, byte commandCode, int dimOrBrightAmount)
        {
            byte function = Convert.ToByte((houseCode << 4) | commandCode);
            byte functionNotification;

            if (commandCode.Equals(X10CommandCodes.Bright) || commandCode.Equals(X10CommandCodes.Dim))
            {
                byte normalizedDimBrighten = Convert.ToByte(Math.Floor(dimOrBrightAmount * .01 * 22));
                functionNotification = Convert.ToByte((normalizedDimBrighten << 3) | X10DeviceCommands.FunctionNotificationBaseCode);
            }
            else
            {
                functionNotification = X10DeviceCommands.FunctionNotificationBaseCode;
            }
            return new[] { functionNotification, function };
        }

        private void HandleResponse(byte response)
        {
            if (response == X10DeviceCommands.PowerFailureCode)
                SetClock();
            else if (response == X10DeviceCommands.PollSignalCode)
                HandleIncomingData();
        }

        private void AcknowledgePoll()
        {
            WriteBytes(new byte[] { X10DeviceCommands.PollAcknowledgementCode });
        }

        private void HandleIncomingData()
        {
            lock (_syncRoot)
            {
                byte[] rawDataLength;

                do
                {
                    AcknowledgePoll();
                    rawDataLength = GetBytes();
                } while (rawDataLength[0] == X10DeviceCommands.PollSignalCode);

                byte dataLength = (byte)(rawDataLength[0] - 1);
                GetBytes();
                List<byte> dataBuffer = new List<byte>();
                for (int i = 0; i <= dataLength - 1; i++)
                    dataBuffer.Add(GetBytes()[0]);
            }
        }

        private void SetClock()
        {
            DateTime now = DateTime.Now;
            byte[] clockCommand = new byte[7];
            clockCommand[0] = 0x9b;
            clockCommand[1] = (byte)now.Second;
            int totalMinutes = (now.Hour * 60) + now.Minute;
            clockCommand[2] = (byte)(totalMinutes % 120);
            clockCommand[3] = (byte)(now.Hour / 2);
            clockCommand[4] = (byte)(now.DayOfYear & 0xff);
            clockCommand[5] = (byte)(2 ^ (int)now.DayOfWeek);
            if (now.DayOfYear > 255)
                clockCommand[5] = (byte)(clockCommand[5] | 0x80);
            clockCommand[6] = (byte)(_houseCodeToMonitor << 4);

            WriteBytes(clockCommand);
            GetBytes();

            WriteBytes(new byte[] { X10DeviceCommands.AcknowledgeCode });
            GetBytes();
        }

        public byte[] GetBytes()
        {
            byte[] data = null;

            try
            {
                if (_serialDevice != null)
                {
                    CancellationToken token = _readCancellationTokenSource.Token;
                    token.ThrowIfCancellationRequested();

                    _dataReaderObject = new DataReader(_serialDevice.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
                    uint bytesRead = _dataReaderObject.LoadAsync(1024).AsTask(token).Result;
                    if (bytesRead > 0)
                         _dataReaderObject.ReadBytes(data);
                }
            }
            catch (Exception ex)
            {
                if (ex.GetType().Name != "TaskCanceledException")
                    throw;
            }
            finally
            {
                _dataReaderObject?.DetachStream();
                _dataReaderObject = null;
            }

            return data;
        }
        #endregion

        public void WriteBytes([ReadOnlyArray] byte[] bytes)
        {
            try
            {
                if (_serialDevice != null)
                {
                    CancellationToken token = _readCancellationTokenSource.Token;
                    token.ThrowIfCancellationRequested();

                    _dataWriteObject = new DataWriter(_serialDevice.OutputStream);
                    if (bytes.Length != 0)
                    {
                        _dataWriteObject.WriteBytes(bytes);
                        _dataWriteObject.StoreAsync().AsTask(token).Wait(token);
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.GetType().Name != "TaskCanceledException")
                    throw;
            }
            finally
            {
                _dataWriteObject?.DetachStream();
                _dataWriteObject = null;
            }
        }
    }
}
