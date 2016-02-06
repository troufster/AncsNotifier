using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AncsNotifier
{
    //https://developer.apple.com/library/ios/documentation/CoreBluetooth/Reference/AppleNotificationCenterServiceSpecification/Specification/Specification.html
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NotificationSourceData
    {
        public byte EventId;
        public EventFlags EventFlags;
        public CategoryId CategoryId;
        public byte CategoryCount;

        public UInt32 NotificationUID;
    }

    public enum CategoryId : byte
    {
        Other = 0,
        IncomingCall = 1,
        MissedCall = 2,
        Voicemail = 3,
        Social = 4,
        Schedule = 5,
        Email = 6,
        News = 7,
        HealthAndFitness = 8,
        BusinessAndFinance = 9,
        Location = 10,
        Entertainment = 11
        //Todo: reserved to 255
    }

    [Flags]
    public enum EventFlags : byte
    {
        EventFlagSilent = 1 << 0,
        EventFlagImportant = 1 << 1,
        EventFlagPreExisting = 1 << 2,
        EventFlagPositiveAction = 1 << 3,
        EventFlagNegativeAction = 1 << 4,
        Reserved1 = 1 << 5,
        Reserved2 = 1 << 6,
        Reserved3 = 1 << 7
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NotificationActionData
    {
        public byte CommandId;
        public UInt32 NotificationUID;
        public byte ActionId;
    }


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GetNotificationAttributesData
    {
        public byte CommandId;
        public UInt32 NotificationUID;
        public byte AttributeId1;
        public UInt16 AttributeId1MaxLen;
        public byte AttributeId2;
        public UInt16 AttributeId2MaxLen;

    }

    public enum NotificationAttribute : byte
    {
        AppIdentifier = 0x0,
        Title = 0x1,
        Subtitle = 0x2,
        Message = 0x3
    }

    public class PlainNotification
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public uint Uid { get; set; }
        public EventFlags? EventFlags { get; set; }

        public bool Positive => EventFlags != null && EventFlags.Value.HasFlag(AncsNotifier.EventFlags.EventFlagPositiveAction);

        public bool Negative => EventFlags != null && EventFlags.Value.HasFlag(AncsNotifier.EventFlags.EventFlagNegativeAction);
    }
}
