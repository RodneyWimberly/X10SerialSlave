using System;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Gpio;
using Windows.Devices.Spi;

namespace nRF24L01
{
    public sealed class Rf24
    {
        internal const byte PacketSize = 10;

        private bool _isPlusModel;
        private bool _isWideBand;
        private readonly GpioPin _cePin;
        private readonly SpiDevice _spiDevice;
        private RadioMode _radioMode;
        public RadioMode RadioMode
        {
            get { return _radioMode; }
            set
            {
                _radioMode = value;
                if (_radioMode == RadioMode.Receive)
                {
                    WriteRegister(Constants.CONFIG, Constants.CONFIG_PWR_UP | Constants.CONFIG_PRIM_RX);
                }
                else
                {
                    WriteRegister(Constants.CONFIG, Constants.CONFIG_PWR_UP | Constants.CONFIG_PRIM_RX);
                    ChipEnable(false);
                }
            }
        }

        public bool IsDataAvailable => (GetStatus() & (BitValue(Constants.MASK_RX_DR))) > 0;
        public RadioModels Model => _isPlusModel ? RadioModels.nRF24L01P : RadioModels.nRF24L01;
        public string ModelName => Constants.RadioModelStrings[(int)Model];

        public Rf24(GpioPin cePin, SpiDevice spiDevice)
        {
            _isPlusModel = false;
            _isWideBand = false;
            _cePin = cePin;
            _spiDevice = spiDevice;
            _cePin.SetDriveMode(GpioPinDriveMode.Output);
        }

        public void Initialize()
        {
            WriteRegister(Constants.RX_PW_P0, PacketSize);
            WriteRegister(Constants.RX_PW_P1, PacketSize);
            WriteRegister(Constants.EN_AA, 0x00);



            FlushTransmitBuffer();
            FlushReceiveBuffer();

            RadioMode = RadioMode.Receive;
        }

        public byte ReadRegister(byte register)
        {
            return ReadRegister(register, 1)[0];
        }

        public byte[] ReadRegister(byte register, int length)
        {
            ChipEnable(false);
            byte[] readBuffer = new byte[length];
            _spiDevice.TransferFullDuplex(new[]
            {
                (byte)(Constants.W_REGISTER | (Constants.REGISTER_MASK & register)),
                Constants.NOP
            },
            readBuffer);
            ChipEnable(true);

            return readBuffer;
        }

        public void WriteRegister(byte register, byte value)
        {
            ChipEnable(false);
            _spiDevice.Write(new[] { (byte)(Constants.W_REGISTER | (Constants.REGISTER_MASK & register)), value });
            ChipEnable(true);
        }

        public void ChipEnable(bool enabled)
        {
            _cePin.Write(enabled ? GpioPinValue.High : GpioPinValue.Low);
            Task.Delay(50).Wait();
        }

        public void FlushTransmitBuffer()
        {
            _spiDevice.Write(new[] { Constants.FLUSH_TX });
        }

        public void FlushReceiveBuffer()
        {
            _spiDevice.Write(new[] { Constants.FLUSH_RX });
        }

        public void TransmitPayload([ReadOnlyArray] byte[] data)
        {
            RadioMode = RadioMode.Transmit;

            _spiDevice.Write(new[] {
                Constants.W_TX_PAYLOAD,
                data[0],
                data[1],
                data[2],
                data[3],
                data[4],
                data[5],
                data[6],
                data[8],
                data[9]
            });

            ChipEnable(true);
            ChipEnable(false);

            RadioMode = RadioMode.Receive;
        }

        public byte[] ReceivePayload()
        {
            byte[] readBuffer = new byte[11];

            ChipEnable(false);
            _spiDevice.TransferFullDuplex(new[] {
                Constants.R_RX_PAYLOAD,
                Constants.NOP,
                Constants.NOP,
                Constants.NOP,
                Constants.NOP,
                Constants.NOP,
                Constants.NOP,
                Constants.NOP,
                Constants.NOP,
                Constants.NOP,
                Constants.NOP
            }, readBuffer);

            ChipEnable(false);
            _spiDevice.Write(new byte[] {
                Constants.W_REGISTER + Constants.STATUS,
                Constants.STATUS_DEFAULT_VAL | Constants.STATUS_RX_DR
            });

            return readBuffer;
        }

        public byte GetStatus()
        {
            ChipEnable(false);
            byte[] readBuffer = new byte[1];
            _spiDevice.TransferFullDuplex(new[] { Constants.NOP }, readBuffer);
            ChipEnable(false);

            return readBuffer[0];
        }

        public bool TestCarrier()
        {
            return (ReadRegister(Constants.CD) & 1) == 1;
        }

        public bool TestRpd()
        {
            return (ReadRegister(Constants.RPD) & 1) == 1;
        }

        public string GetAddressRegister(string name, byte register, int quantity)
        {
            string registerValue = "\t" + name + (name.Length < 8 ? "\t" : "") + " = ";
            while (quantity-- >= 0)
            {
                byte[] values = ReadRegister(register++, 5);
                registerValue += " 0x" + BitConverter.ToString(values).Replace("-", string.Empty);
            }

            return registerValue;
        }

        public string GetByteRegister(string name, byte register, int quantity)
        {
            string registerValue = "\t" + name + (name.Length < 8 ? "\t" : "") + " = ";
            while (quantity-- >= 0)
            {
                byte[] values = ReadRegister(register++, 1);
                registerValue += " 0x" + BitConverter.ToString(values).Replace("-", string.Empty);
            }

            return registerValue;
        }

        public string GetDetails()
        {
            StringBuilder sb = new StringBuilder();

            byte status = GetStatus();
            sb.AppendFormat("STATUS\t\t = 0x{0} RX_DR={1} TX_DS={2} MAX_RT={3} RX_P_NO={4} TX_FULL={5}\r\n",
                status,
                status & BitValue(Constants.RX_DR),
                status & BitValue(Constants.TX_DS),
                status & BitValue(Constants.MAX_RT),
                (status >> Constants.RX_P_NO) & 7,
                status & BitValue(Constants.TX_FULL));

            sb.AppendLine(GetAddressRegister("RX_ADDR_P0-1", Constants.RX_ADDR_P0, 2));
            sb.AppendLine(GetByteRegister("RX_ADDR_P2-5", Constants.RX_ADDR_P2, 4));
            sb.AppendLine(GetAddressRegister("TX_ADDR", Constants.TX_ADDR, 1));

            sb.AppendLine(GetByteRegister("RX_PW_P0-6", Constants.RX_PW_P0, 6));
            sb.AppendLine(GetByteRegister("EN_AA", Constants.EN_AA, 1));
            sb.AppendLine(GetByteRegister("EN_RXADDR", Constants.EN_RXADDR, 1));
            sb.AppendLine(GetByteRegister("RF_CH", Constants.RF_CH, 1));
            sb.AppendLine(GetByteRegister("RF_SETUP", Constants.RF_SETUP, 1));
            sb.AppendLine(GetByteRegister("CONFIG", Constants.CONFIG, 1));
            sb.AppendLine(GetByteRegister("DYNPD/FEATURE", Constants.DYNPD, 2));

            sb.AppendLine("Data Rate\t = " + Constants.DataRateStrings[(int)GetDataRate()]);
            sb.AppendLine("Model\t\t = " + Model);
            sb.AppendLine("CRC Length\t = " + Constants.CrcLengthStrings[(int)GetCrcLength()]);
            sb.AppendLine("PA Power\t = " + Constants.PowerLevelStrings[(int)GetPowerLevel()]);

            Debug.WriteLine(sb);
            return sb.ToString();
        }


        public DataRates GetDataRate()
        {
            DataRates dataRate = DataRates.DataRate250Kbps;
            byte setup = (byte)(ReadRegister(Constants.RF_SETUP) & (1 << Constants.RF_DR_LOW | 1 << Constants.RF_DR_HIGH));
            if (setup == BitValue(Constants.RF_DR_LOW))
                dataRate = DataRates.DataRate250Kbps;
            else if (setup == BitValue(Constants.RF_DR_HIGH))
                dataRate = DataRates.DataRate2Mbps;
            else
                dataRate = DataRates.DataRate1Mbps;
            return dataRate;
        }

        public bool SetDataRate(DataRates dataRate)
        {
            bool success = false;
            byte setup = ReadRegister(Constants.RF_SETUP);
            // HIGH and LOW '00' is 1Mbs - our default
            _isWideBand = false;
            setup &= (byte)(~(BitValue(Constants.RF_DR_LOW) | BitValue(Constants.RF_DR_HIGH)));
            if (dataRate == DataRates.DataRate250Kbps)
            {
                // Must set the RF_DR_LOW to 1; RF_DR_HIGH (used to be RF_DR) is already 0
                // Making it '10'.
                _isWideBand = false;
                setup |= BitValue(Constants.RF_DR_LOW);
            }
            else
            {
                // Set 2Mbs, RF_DR (RF_DR_HIGH) is set 1
                // Making it '01'
                if (dataRate == DataRates.DataRate2Mbps)
                {
                    _isWideBand = true;
                    setup |= BitValue(Constants.RF_DR_HIGH);
                }
                else
                {
                    // 1Mbs
                    _isWideBand = false;
                }
            }
            WriteRegister(Constants.RF_SETUP, setup);

            // Verify Results
            if (ReadRegister(Constants.RF_SETUP) == setup)
                success = true;
            else
                _isWideBand = false;

            return success;
        }

        public CrcLengths GetCrcLength()
        {
            CrcLengths crcLength = CrcLengths.CrcDisabled;
            byte config = (byte)(ReadRegister(Constants.CONFIG) & (BitValue(Constants.CRCO) | BitValue(Constants.EN_CRC)));
            if ((config & BitValue(Constants.EN_CRC)) == 1)
                crcLength = (config & BitValue(Constants.CRCO)) == 1 ? CrcLengths.Crc16Bit : CrcLengths.Crc8Bit;

            return crcLength;
        }

        public void SetCrcLength(CrcLengths crcLength)
        {
            byte config = (byte)(ReadRegister(Constants.CONFIG) & ~(BitValue(Constants.CRCO) | BitValue(Constants.CRCO)));
            if (crcLength == CrcLengths.CrcDisabled)
            {
                // Do nothing, we turned it off above. 
            }
            else if (crcLength == CrcLengths.Crc8Bit)
            {
                config |= BitValue(Constants.EN_CRC);
            }
            else
            {
                config |= BitValue(Constants.EN_CRC);
                config |= BitValue(Constants.CRCO);
            }

            WriteRegister(Constants.CONFIG, config);
        }

        public void DisableCrc()
        {
            byte disable = (byte)(ReadRegister(Constants.CONFIG) & ~BitValue(Constants.EN_CRC));
            WriteRegister(Constants.CONFIG, disable);
        }

        public PowerLevels GetPowerLevel()
        {
            PowerLevels powerLevel = PowerLevels.PowerLevelError;
            byte setup = (byte)(ReadRegister(Constants.RF_SETUP) & (BitValue(Constants.RF_PWR_LOW) | BitValue(Constants.RF_PWR_HIGH)));
            if (setup == (BitValue(Constants.RF_PWR_LOW) | BitValue(Constants.RF_PWR_HIGH)))
                powerLevel = PowerLevels.PowerLevelMax;
            else if (setup == BitValue(Constants.RF_PWR_HIGH))
                powerLevel = PowerLevels.PowerLevelHigh;
            else if (setup == BitValue(Constants.RF_PWR_LOW))
                powerLevel = PowerLevels.PowerLevelLow;
            else
                powerLevel = PowerLevels.PowerLevelMin;
            return powerLevel;
        }

        public void SetPowerLevel(PowerLevels powerLevel)
        {
            byte setup = ReadRegister(Constants.RF_SETUP);
            setup &= (byte)(~(BitValue(Constants.RF_PWR_LOW) | BitValue(Constants.RF_PWR_HIGH)));
            if (powerLevel == PowerLevels.PowerLevelMax)
                setup |= (byte)(BitValue(Constants.RF_PWR_LOW) | BitValue(Constants.RF_PWR_HIGH));
            else if (powerLevel == PowerLevels.PowerLevelHigh)
                setup |= BitValue(Constants.RF_PWR_HIGH);
            else if (powerLevel == PowerLevels.PowerLevelLow)
                setup |= BitValue(Constants.RF_PWR_LOW);
            else if (powerLevel == PowerLevels.PowerLevelMin)
            {
                // nothing
            }
            else if (powerLevel == PowerLevels.PowerLevelError)
                setup |= (byte)(BitValue(Constants.RF_PWR_LOW) | BitValue(Constants.RF_PWR_HIGH));

            WriteRegister(Constants.RF_SETUP, setup);
        }

        public void SetRetries(byte delay, byte count)
        {
            WriteRegister(Constants.SETUP_RETR, (byte)((delay & 0xf) << Constants.ARD | (count & 0xf) << Constants.ARC));
        }

        private byte BitValue(byte mask)
        {
            return (byte)(1 << mask);
        }
    }
}
