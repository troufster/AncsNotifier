using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;
using Buffer = Windows.Storage.Streams.Buffer;

namespace AncsNotifier
{
    public class Advertiser
    {
        private static readonly byte[] SolicitationData = {
             // flags
                0x02,
                0x01, //GAP_ADTYPE_FLAGS
                0x02, // GAP_ADTYPE_FLAGS_GENERAL
                //Solicitation
                0x11,
                0x15, //GAP_ADTYPE_SERVICES_LIST_128BIT
                // ANCS service UUID (little endian)
                0xD0, 0x00, 0x2D, 0x12, 0x1E, 0x4B, 0x0F, 0xA4, 0x99, 0x4E, 0xCE, 0xB5, 0x31, 0xF4, 0x05, 0x79
            };

        private static readonly UInt16 ManufacturerId = 0x010E;

        private readonly BluetoothLEAdvertisementPublisher _publisher;

        public Advertiser()
        {        
            var manufacturerData = new BluetoothLEManufacturerData(ManufacturerId, (new byte[] { 0x12, 0x34 }).AsBuffer());

            var advertisment = new BluetoothLEAdvertisement();

            var data = new BluetoothLEAdvertisementDataSection {Data = SolicitationData.AsBuffer()};

            advertisment.DataSections.Add(data);
            advertisment.ManufacturerData.Add(manufacturerData);

            this._publisher = new BluetoothLEAdvertisementPublisher(advertisment);
        }

        public void Advertise()
        {
            try
            {
                this._publisher.Start();
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }
}
