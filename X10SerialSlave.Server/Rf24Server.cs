using nRF24L01;
using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Enumeration;
using Windows.Devices.Gpio;
using Windows.Devices.Spi;

namespace X10SerialSlave.Server
{
    public sealed class Rf24Server : IX10Controller
    {
        private Radio _radio;

        public byte[] GetBytes()
        {
            //return _rf.ReceivePayload();
            return null;
        }

        public async void Initialize()
        {
            GpioPin cePin = GpioController.GetDefault().OpenPin(26);

            SpiConnectionSettings settings = new SpiConnectionSettings(0)
            {
                ClockFrequency = 1000000,
                Mode = SpiMode.Mode0
            };

            string spiAqs = SpiDevice.GetDeviceSelector("SPI0");
            DeviceInformationCollection devicesInfo = await DeviceInformation.FindAllAsync(spiAqs);
            SpiDevice spiDevice = await SpiDevice.FromIdAsync(devicesInfo[0].Id, settings);

            _radio = new Radio(cePin, spiDevice);
            _radio.Begin();
            string details = _radio.GetDetails();
        }

        public void WriteBytes([ReadOnlyArray] byte[] bytes)
        {
            //_rf.TransmitPayload(bytes);
        }
    }
}
