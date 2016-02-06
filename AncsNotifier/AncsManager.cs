using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace AncsNotifier
{
    public class AncsManager
    {
        private readonly Guid _ancsServiceUiid = new Guid("7905F431-B5CE-4E99-A40F-4B1E122D00D0");
        private readonly Guid _notificationSourceCharacteristicUuid = new Guid("9FBF120D-6301-42D9-8C58-25E699A21DBD");
        private readonly Guid _controlPointCharacteristicUuid = new Guid("69D1D8F3-45E1-49A8-9821-9BBDFDAAD9D9");
        private readonly Guid _dataSourceCharacteristicUuid = new Guid("22EAC6E9-24D6-4BB5-BE44-B36ACE7C7BFB");

        public DeviceInformation AncsDevice { get; set; }
        public GattDeviceService AncsService { get; set; }

        public GattCharacteristic NotificationSourceCharacteristic { get; set; }
        public GattCharacteristic ControlPointCharacteristic { get; set; }
        public GattCharacteristic DataSourceCharacteristic { get; set; }

        public event Action<PlainNotification> OnNotification;
        public event Action<string> OnStatusChange;
        public static Action<IActivatedEventArgs> OnUpdate = args => {};

        public Dictionary<uint, EventFlags> FlagCache = new Dictionary<uint, EventFlags>(); 

        public AncsManager()
        {         
            OnUpdate = OnUpdateReceived;
        }

        private void OnUpdateReceived(IActivatedEventArgs activatedEventArgs)
        {
            var b = 2;
        }

        public async void Connect()
        {
            //Find a device that is advertising the ancs service uuid
            var serviceDeviceSelector = GattDeviceService.GetDeviceSelectorFromUuid(_ancsServiceUiid);
            var devices = await DeviceInformation.FindAllAsync(serviceDeviceSelector, null);
            this.AncsDevice = devices.First();

            //Resolve the service
            this.AncsService = await GattDeviceService.FromIdAsync(this.AncsDevice.Id);

            this.AncsService.Device.ConnectionStatusChanged += DeviceOnConnectionStatusChanged;

            //Get charasteristics of service
            this.NotificationSourceCharacteristic = this.AncsService.GetCharacteristics(_notificationSourceCharacteristicUuid).First();
            this.ControlPointCharacteristic = this.AncsService.GetCharacteristics(_controlPointCharacteristicUuid).First();
            this.DataSourceCharacteristic = this.AncsService.GetCharacteristics(_dataSourceCharacteristicUuid).First();

        }

        private async void DeviceOnConnectionStatusChanged(BluetoothLEDevice device, object args)
        {
            if (device.ConnectionStatus == BluetoothConnectionStatus.Connected)
            {
                //Get stuff up and running
                OnStatusChange?.Invoke("Connected");           

                if (
                    this.NotificationSourceCharacteristic.CharacteristicProperties.HasFlag(
                        GattCharacteristicProperties.Notify))
                {
                    this.NotificationSourceCharacteristic.ValueChanged += NotificationSourceCharacteristicOnValueChanged;

                    // Set the notify enable flag
                    try
                    {
                        await
                            this.NotificationSourceCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                                GattClientCharacteristicConfigurationDescriptorValue.Notify);
                    }
                    catch (Exception ex)
                    {

                    }
                }

                this.DataSourceCharacteristic.ValueChanged += DataSourceCharacteristicOnValueChanged;
                await
                    this.DataSourceCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify);
            }

            if (device.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                //Stop doing stuff
                this.DataSourceCharacteristic.ValueChanged -= DataSourceCharacteristicOnValueChanged;
                this.NotificationSourceCharacteristic.ValueChanged -= NotificationSourceCharacteristicOnValueChanged;

                OnStatusChange?.Invoke("Disconnected");
            }
        }

        private async void DataSourceCharacteristicOnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var stream = args.CharacteristicValue.AsStream();
            var br = new BinaryReader(stream);

            var cmdId = br.ReadByte();
            var notUid = br.ReadUInt32();
            var attr1 = (NotificationAttribute)br.ReadByte();
            var attr1len = br.ReadUInt16();
            var attr1val = br.ReadChars(attr1len);
            var attr2 = (NotificationAttribute) br.ReadByte();
            var attr2len = br.ReadUInt16();
            var attr2val = br.ReadChars(attr2len);

            EventFlags? flags = null;

            if(FlagCache.ContainsKey(notUid))
            {
                flags = FlagCache[notUid];
            }

            var not = new PlainNotification()
            {
                EventFlags = flags,
                Uid = notUid,
                Title = new string(attr1val),
                Message = new string(attr2val)
            };

            OnNotification?.Invoke(not);
        }

        private async void NotificationSourceCharacteristicOnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            //Received 8 bytes about some kind of notification
            var valueBytes = args.CharacteristicValue.ToArray();
            var dat = ByteArrayToNotificationSourceData(valueBytes);


            if (dat.EventFlags.HasFlag(EventFlags.EventFlagPreExisting))
            {
                //We dont care about old notifications
                return;
            }


            FlagCache[dat.NotificationUID] = dat.EventFlags;

            //Ask for more data through the control point characteristic
            var attributes = new GetNotificationAttributesData
            {
                CommandId = 0x0,
                NotificationUID = dat.NotificationUID,
                AttributeId1 = (byte) NotificationAttribute.Title,
                AttributeId1MaxLen = 16,
                AttributeId2 = (byte) NotificationAttribute.Message,
                AttributeId2MaxLen = 32
            };

            var bytes = StructureToByteArray(attributes);

            try
            {
                var status =
                    await
                        this.ControlPointCharacteristic.WriteValueAsync(bytes.AsBuffer(),
                            GattWriteOption.WriteWithResponse);
            }
            catch (Exception)
            {
                
            }
        }

        private NotificationSourceData ByteArrayToNotificationSourceData(byte[] packet)
        {
            GCHandle pinnedPacket = GCHandle.Alloc(packet, GCHandleType.Pinned);
            var msg = Marshal.PtrToStructure<NotificationSourceData>(pinnedPacket.AddrOfPinnedObject());
            pinnedPacket.Free();

            return msg;
        }

        private byte[] StructureToByteArray(object obj)
        {
            int len = Marshal.SizeOf(obj);

            byte[] arr = new byte[len];

            IntPtr ptr = Marshal.AllocHGlobal(len);

            Marshal.StructureToPtr(obj, ptr, true);

            Marshal.Copy(ptr, arr, 0, len);

            Marshal.FreeHGlobal(ptr);

            return arr;
        }
    }
}
