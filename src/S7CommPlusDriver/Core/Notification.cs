#region License
/******************************************************************************
 * S7CommPlusDriver
 *
 * Copyright (C) 2023 Thomas Wiens, th.wiens@gmx.de
 *
 * This file is part of S7CommPlusDriver.
 *
 * S7CommPlusDriver is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as
 * published by the Free Software Foundation, either version 3 of the
 * License, or (at your option) any later version.
 /****************************************************************************/
#endregion

using System;
using System.Collections.Generic;
using System.IO;

namespace S7CommPlusDriver
{
    internal class Notification
    {
        public byte ProtocolVersion { get; set; }

        public UInt32 SubscriptionObjectId;
        public UInt16 Unknown2;
        public UInt16 Unknown3;
        public UInt16 Unknown4;

        public byte NotificationCreditTick;
        public UInt32 NotificationSequenceNumber;
        public byte SubscriptionChangeCounter;

        public DateTime Add1Timestamp;
        public byte Add1SubscriptionChangeCounter;

        public Dictionary<UInt32, PValue> Values;       // Item reference number, Value
        public Dictionary<UInt32, byte> ReturnValues;   // Item reference number, ReturnValue
        public Dictionary<UInt32, UInt32> OnlineStatusTableValues; // Item reference number, raw status-table word

        public UInt32 P2SubscriptionObjectId;           // for alarm object
        public UInt16 P2Unknown1;                       // for alarm object
        public byte P2ReturnValue;                      // for alarm object
        public List<PObject> P2Objects;                 // for alarm object
        public byte[] TrailingBytes;                    // for non-alarm notification extensions

        public Notification(byte protocolVersion)
        {
            ProtocolVersion = protocolVersion;
            Values = new Dictionary<UInt32, PValue>();
            ReturnValues = new Dictionary<UInt32, byte>();
            OnlineStatusTableValues = new Dictionary<UInt32, UInt32>();
        }

        public int Deserialize(Stream buffer)
        {
            int ret = 0;
            byte subscrccnt;
            byte item_return_value;
            UInt32 itemref;
            byte PeekByte;

            ret += S7p.DecodeUInt32(buffer, out SubscriptionObjectId);
            ret += S7p.DecodeUInt16(buffer, out Unknown2);
            ret += S7p.DecodeUInt16(buffer, out Unknown3);
            ret += S7p.DecodeUInt16(buffer, out Unknown4);

            ret += S7p.DecodeByte(buffer, out NotificationCreditTick);
            ret += S7p.DecodeUInt32Vlq(buffer, out NotificationSequenceNumber);
            ret += S7p.DecodeByte(buffer, out subscrccnt);
            if (subscrccnt > 0) {
                SubscriptionChangeCounter = subscrccnt;
            }
            else
            {
                // Newer versions of 1500 if subscrccnt ==0:
                // If this is zero, then an 8 byte UTC Timestamp on microsecond basis follows,
                // where the first byte should be always zero (in the 'near' future).
                buffer.Position -= 1;// Set position back
                ulong timestamp;
                ret += S7p.DecodeUInt64(buffer, out timestamp);
                ulong epochTicks = 621355968000000000; // Unix Time (UTC) on 1st January 1970.
                // Convert to .Net DateTime
                Add1Timestamp = new DateTime((long)(timestamp * 10 + epochTicks), DateTimeKind.Utc);
                ret += S7p.DecodeByte(buffer, out Add1SubscriptionChangeCounter);
            }
            // Return value: If the value != 0 then follows a dataset with the common known structure.
            // If an access error occurs, we have here an error-value, in this case datatype==NULL.
            // Known observed return codes from different controllers and generations:
            //  hex       bin       ref-id  value   description
            //  0x03 = 0000 0011 -> ntohl   -       Addressing error (S7-1500 - Plcsim), like 0x13
            //  0x13 = 0001 0011 -> ntohl   -       Addressing error (S7-1200) and 1500-Plcsim
            //  0x81 = 1000 0001 ->         object  Standard object starts with 0xa1 (only in protocol version v1?)
            //  0x83 = 1000 0011 ->         value   Standard value structure, then notification value-list (only in protocol version v1?)
            //  0x92 = 1001 0010 -> ntohl   value   Success (S7-1200)
            //  0x9b = 1001 1011 -> vlq32   value   Seen on 1500 and 1200. Following ID or number, then flag, type, value
            //  0x9c = 1001 1100 -> ntohl   ?       Online with variable status table (S7-1200), structure seems to be completely different
            do
            {
                ret += S7p.DecodeByte(buffer, out item_return_value);
                var returnCode = (S7CommPlusNotificationReturnCode)item_return_value;
                switch (returnCode)
                {
                    case S7CommPlusNotificationReturnCode.EndOfList:
                        break;
                    case S7CommPlusNotificationReturnCode.ValueWithUInt32Reference:
                        // Item reference number: Is sent to plc in the subscription-telegram for the addresses.
                        ret += S7p.DecodeUInt32(buffer, out itemref);
                        Values.Add(itemref, PValue.Deserialize(buffer));
                        break;
                    case S7CommPlusNotificationReturnCode.ValueWithVlqReference:
                        ret += S7p.DecodeUInt32Vlq(buffer, out itemref);
                        Values.Add(itemref, PValue.Deserialize(buffer));
                        break;
                    case S7CommPlusNotificationReturnCode.OnlineStatusTable:
                        // Observed online status-table item shape:
                        // 9C rr ss ss vv, where rr is the subscription item reference,
                        // ss ss is a status-table field id, and vv is the compact status value.
                        ret += S7p.DecodeUInt32(buffer, out var onlineStatusTableValue);
                        OnlineStatusTableValues[onlineStatusTableValue >> 24] = onlineStatusTableValue;
                        break;
                    case S7CommPlusNotificationReturnCode.AddressingError1200:
                    case S7CommPlusNotificationReturnCode.AddressingError:
                        ret += S7p.DecodeUInt32(buffer, out itemref);
                        ReturnValues.Add(itemref, item_return_value);
                        break;
                    //case 0x81: //Only in protocol version v1, but also used in S7-1500 in part 2 for ProgramAlarm
                    case S7CommPlusNotificationReturnCode.LegacyValue:
                        // Probably only in protocol version v1
                        throw new NotSupportedException(
                            $"Unsupported legacy notification return code 0x{item_return_value:X2} at PDU offset {buffer.Position} of {buffer.Length}.");
                    default:
                        // unknown return value
                        throw new NotSupportedException(
                            $"Unsupported notification return code 0x{item_return_value:X2} at PDU offset {buffer.Position} of {buffer.Length}.");
                }
            } while (item_return_value != 0);

            // If next byte is not zero, an alarm notification object may follow
            ret += S7p.DecodeByte(buffer, out PeekByte);
            // Set position back
            buffer.Position -= 1;
            if (PeekByte != 0)
            {
                var part2Start = buffer.Position;
                ret += S7p.DecodeUInt32(buffer, out P2SubscriptionObjectId);
                ret += S7p.DecodeUInt16(buffer, out P2Unknown1);
                ret += S7p.DecodeByte(buffer, out P2ReturnValue);
                // It's not known if there are more than one object (as List), each object has
                // it's return value, or if there is really only one.
                // I wasn't able to produce a notification with more than one.
                if ((S7CommPlusNotificationReturnCode)P2ReturnValue == S7CommPlusNotificationReturnCode.AlarmObject)
                {
                    P2Objects = new List<PObject>();
                    ret += S7p.DecodeObjectList(buffer, ref P2Objects);
                }
                else
                {
                    buffer.Position = part2Start;
                    var remaining = (int)(buffer.Length - buffer.Position);
                    TrailingBytes = new byte[remaining];
                    if (remaining > 0)
                    {
                        var readTotal = 0;
                        while (readTotal < remaining)
                        {
                            var read = buffer.Read(TrailingBytes, readTotal, remaining - readTotal);
                            if (read == 0)
                            {
                                throw new EndOfStreamException();
                            }
                            readTotal += read;
                        }
                        ret += remaining;
                    }
                }
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "";
            s += "<Notification>" + Environment.NewLine;
            s += "<ProtocolVersion>" + ProtocolVersion.ToString() + "</ProtocolVersion>" + Environment.NewLine;
            s += "<SubscriptionObjectId>" + SubscriptionObjectId.ToString() + "</SubscriptionObjectId>" + Environment.NewLine;
            s += "<Unknown2>" + Unknown2.ToString() + "</Unknown2>" + Environment.NewLine;
            s += "<Unknown3>" + Unknown3.ToString() + "</Unknown3>" + Environment.NewLine;
            s += "<Unknown4>" + Unknown4.ToString() + "</Unknown4>" + Environment.NewLine;
            s += "<NotificationCreditTick>" + NotificationCreditTick.ToString() + "</NotificationCreditTick>" + Environment.NewLine;
            s += "<NotificationSequenceNumber>" + NotificationSequenceNumber.ToString() + "</NotificationSequenceNumber>" + Environment.NewLine;
            s += "<SubscriptionChangeCounter>" + SubscriptionChangeCounter.ToString() + "</SubscriptionChangeCounter>" + Environment.NewLine;
            s += "<Add1Timestamp>" + String.Format("{0}.{1:D03} UTC", Add1Timestamp.ToString(), Add1Timestamp.Millisecond)  + "</Add1Timestamp>" + Environment.NewLine;
            s += "<Add1SubscriptionChangeCounter>" + Add1SubscriptionChangeCounter.ToString() + "</Add1SubscriptionChangeCounter>" + Environment.NewLine;
            s += "<ValueList>" + Environment.NewLine;
            foreach (var value in Values)
            {
                s += "<Value>" + Environment.NewLine;
                s += "<ItemRefId>" + value.Key.ToString() + "</ItemRefId>" + Environment.NewLine;
                s += value.Value.ToString();
                s += "</Value>" + Environment.NewLine;
            }
            s += "</ValueList>" + Environment.NewLine;

            s += "<ReturnValueList>" + Environment.NewLine;
            foreach (var errval in ReturnValues)
            {
                s += "<ReturnValue>" + Environment.NewLine;
                s += "<ItemRefId>" + errval.Key.ToString() + "</ItemRefId>" + Environment.NewLine;
                s += "<ReturnValue>" + errval.Value.ToString() + "</ReturnValue>" + Environment.NewLine;
                s += "<ReturnValueName>" + ((S7CommPlusNotificationReturnCode)errval.Value).ToString() + "</ReturnValueName>" + Environment.NewLine;
                s += "</ReturnValue>" + Environment.NewLine;
            }
            s += "</ReturnValueList>" + Environment.NewLine;
            s += "<OnlineStatusTableValueList>" + Environment.NewLine;
            foreach (var value in OnlineStatusTableValues)
            {
                s += "<OnlineStatusTableValue>" + Environment.NewLine;
                s += "<ItemRefId>" + value.Key.ToString() + "</ItemRefId>" + Environment.NewLine;
                s += "<RawValue>" + value.Value.ToString() + "</RawValue>" + Environment.NewLine;
                s += "</OnlineStatusTableValue>" + Environment.NewLine;
            }
            s += "</OnlineStatusTableValueList>" + Environment.NewLine;
            // For alarm object(s)
            s += "<P2SubscriptionObjectId>" + P2SubscriptionObjectId.ToString() + "</P2SubscriptionObjectId>" + Environment.NewLine;
            s += "<P2Unknown1>" + P2Unknown1.ToString() + "</P2Unknown1>" + Environment.NewLine;
            s += "<P2ReturnValue>" + P2ReturnValue.ToString() + "</P2ReturnValue>" + Environment.NewLine;
            s += "<P2ReturnValueName>" + ((S7CommPlusNotificationReturnCode)P2ReturnValue).ToString() + "</P2ReturnValueName>" + Environment.NewLine;
            s += "<P2Objects>" + Environment.NewLine;
            if (P2Objects != null)
            {
                foreach (var p2o in P2Objects)
                {
                    s += p2o.ToString();
                }
            }
            s += "</P2Objects>" + Environment.NewLine;
            if (TrailingBytes != null)
            {
                s += "<TrailingBytes>" + BitConverter.ToString(TrailingBytes) + "</TrailingBytes>" + Environment.NewLine;
            }
            s += "</Notification>" + Environment.NewLine;
            return s;
        }

        public static Notification DeserializeFromPdu(Stream pdu)
        {
            byte protocolVersion;
            byte opcode;

            // Special handling of ProtocolVersion, which is written to the stream before
            S7p.DecodeByte(pdu, out protocolVersion);
            S7p.DecodeByte(pdu, out opcode);
            if (opcode != Opcode.Notification)
            {
                return null;
            }
            var notif = new Notification(protocolVersion);
            notif.Deserialize(pdu);

            return notif;
        }
    }
}
