using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Background;
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

        public async void OnAction(PlainNotification notification, bool positive)
        {
            //Relay notification action back to device
            var not = new NotificationActionData
            {
                CommandId = 0x02, //CommandIDPerformNotificationAction
                NotificationUID = notification.Uid,
                ActionId =  positive ? ActionId.Positive : ActionId.Negative
            };

            var bytes = StructureToByteArray(not);

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

        private void OnUpdateReceived(IActivatedEventArgs activatedEventArgs)
        {
            var b = 2;
        }

        public async void Connect()
        {
            //Find a device that is advertising the ancs service uuid
            var serviceDeviceSelector = GattDeviceService.GetDeviceSelectorFromUuid(_ancsServiceUiid);
            var devices = await DeviceInformation.FindAllAsync(serviceDeviceSelector, null);
            AncsDevice = devices[0];

            string deviceid = this.AncsDevice.Id;

            //Resolve the service
            this.AncsService = await GattDeviceService.FromIdAsync(deviceid);

            //this.AncsService.Device.ConnectionStatusChanged += DeviceOnConnectionStatusChanged;
            AncsService.Session.SessionStatusChanged += iDeviceOnConnectionStatusChanged;

            //Get charasteristics of service
            var ret = await this.AncsService.GetCharacteristicsForUuidAsync(_notificationSourceCharacteristicUuid);
            this.NotificationSourceCharacteristic = ret.Characteristics[0];

            ret = await AncsService.GetCharacteristicsForUuidAsync(_controlPointCharacteristicUuid);
            this.ControlPointCharacteristic = ret.Characteristics[0];

            ret = await AncsService.GetCharacteristicsForUuidAsync(_dataSourceCharacteristicUuid);
            DataSourceCharacteristic = ret.Characteristics[0];
        }

        private async void iDeviceOnConnectionStatusChanged(GattSession sender, GattSessionStatusChangedEventArgs args)
        {
            if (sender.SessionStatus.Equals(GattSessionStatus.Active))
            {
                //Get stuff up and running
                OnStatusChange?.Invoke("Connected");

                //if (
                //    this.NotificationSourceCharacteristic.CharacteristicProperties.HasFlag(
                //        GattCharacteristicProperties.Notify))
                //{
                //    this.NotificationSourceCharacteristic.ValueChanged += NotificationSourceCharacteristicOnValueChanged;

                //    // Set the notify enable flag
                //    try
                //    {
                //        await
                //            this.NotificationSourceCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                //                GattClientCharacteristicConfigurationDescriptorValue.Notify);
                //    }
                //    catch (Exception ex)
                //    {

                //    }
                //}

                //this.DataSourceCharacteristic.ValueChanged += DataSourceCharacteristicOnValueChanged;
                //await
                //    this.DataSourceCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                //        GattClientCharacteristicConfigurationDescriptorValue.Notify);




                // initialize status
                GattCommunicationStatus status = GattCommunicationStatus.Unreachable;
                var cccdValue = GattClientCharacteristicConfigurationDescriptorValue.None;
                if (NotificationSourceCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate))
                {
                    cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Indicate;
                }

                else if (NotificationSourceCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                {
                    cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Notify;
                }

                try
                {
                    this.NotificationSourceCharacteristic.ValueChanged += NotificationSourceCharacteristicOnValueChanged;

                    // BT_Code: Must write the CCCD in order for server to send indications.
                    // We receive them in the ValueChanged event handler.
                    status = await NotificationSourceCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue);

                    if (status == GattCommunicationStatus.Success)
                    {
                        this.DataSourceCharacteristic.ValueChanged += DataSourceCharacteristicOnValueChanged;
                    }
                    else
                    {
                        //rootPage.NotifyUser($"Error registering for value changes: {status}", NotifyType.ErrorMessage);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    // This usually happens when a device reports that it support indicate, but it actually doesn't.
                    //rootPage.NotifyUser(ex.Message, NotifyType.ErrorMessage);
                }
            }
            else
            {
                //Stop doing stuff
                this.DataSourceCharacteristic.ValueChanged -= DataSourceCharacteristicOnValueChanged;
                this.NotificationSourceCharacteristic.ValueChanged -= NotificationSourceCharacteristicOnValueChanged;

                OnStatusChange?.Invoke("Disconnected");

            }
        }

        public BackgroundTaskRegistration BackgroundNotifierRegistration { get; set; }

        

        private void DataSourceCharacteristicOnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
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
