﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace WinRT
{
#if EMBED
    internal
#else 
    public
#endif
    static class GuidGenerator
    {
        public static Guid GetGUID(Type type)
        {
            return type.GetGuidType().GUID;
        }

        public static Guid GetIID(Type type)
        {
            type = type.GetGuidType();
            if (!type.IsGenericType)
            {
                return type.GUID;
            }
            return (Guid)type.GetField("PIID").GetValue(null);
        }

        public static string GetSignature(Type type)
        {
            var helperType = type.FindHelperType();
            if (helperType != null)
            {
                var sigMethod = helperType.GetMethod("GetGuidSignature", BindingFlags.Static | BindingFlags.Public);
                if (sigMethod != null)
                {
                    return (string)sigMethod.Invoke(null, null);
                }
            }

            type = type.IsInterface ? (type.GetAuthoringMetadataType() ?? type) : type;
            if (type == typeof(object))
            {
                return "cinterface(IInspectable)";
            }

            if (type.IsGenericType)
            {
                var args = type.GetGenericArguments().Select(t => GetSignature(t));
                return "pinterface({" + GetGUID(type) + "};" + String.Join(";", args) + ")";
            }

            if (type.IsValueType)
            {
                switch (type.Name)
                {
                    case "SByte": return "i1";
                    case "Byte": return "u1";
                    case "Int16": return "i2";
                    case "UInt16": return "u2";
                    case "Int32": return "i4";
                    case "UInt32": return "u4";
                    case "Int64": return "i8";
                    case "UInt64": return "u8";
                    case "Single": return "f4";
                    case "Double": return "f8";
                    case "Boolean": return "b1";
                    case "Char": return "c2";
                    case "Guid": return "g16";
                    default:
                        {
                            if (type.IsEnum)
                            {
                                var isFlags = type.CustomAttributes.Any(cad => cad.AttributeType == typeof(FlagsAttribute));
                                return "enum(" + type.FullName + ";" + (isFlags ? "u4" : "i4") + ")";
                            }
                            if (!type.IsPrimitive)
                            {
                                var args = type.GetFields(BindingFlags.Instance | BindingFlags.Public).Select(fi => GetSignature(fi.FieldType));
                                return "struct(" + type.FullName + ";" + String.Join(";", args) + ")";
                            }
                            throw new InvalidOperationException("unsupported value type");
                        }
                }
            }

            if (type == typeof(string))
            {
                return "string";
            }

            if (Projections.TryGetDefaultInterfaceTypeForRuntimeClassType(type, out Type iface))
            {
                return "rc(" + type.FullName + ";" + GetSignature(iface) + ")";
            }

            if (type.IsDelegate())
            {
                return "delegate({" + GetGUID(type) + "})";
            }

            return "{" + type.GUID.ToString() + "}";
        }

        private static Guid encode_guid(Span<byte> data)
        {
            if (BitConverter.IsLittleEndian)
            {
                // swap bytes of int a
                byte t = data[0];
                data[0] = data[3];
                data[3] = t;
                t = data[1];
                data[1] = data[2];
                data[2] = t;
                // swap bytes of short b
                t = data[4];
                data[4] = data[5];
                data[5] = t;
                // swap bytes of short c and encode rfc time/version field
                t = data[6];
                data[6] = data[7];
                data[7] = (byte)((t & 0x0f) | (5 << 4));
                // encode rfc clock/reserved field
                data[8] = (byte)((data[8] & 0x3f) | 0x80);
            }
#if !NET
            return new Guid(data.Slice(0, 16).ToArray());
#else
            return new Guid(data[0..16]);
#endif
        }

        private readonly static Guid wrt_pinterface_namespace = new(0xd57af411, 0x737b, 0xc042, 0xab, 0xae, 0x87, 0x8b, 0x1e, 0x16, 0xad, 0xee);

        public static Guid CreateIID(Type type)
        {
            var sig = GetSignature(type);
            if (!type.IsGenericType)
            {
                return new Guid(sig);
            }
#if !NET
            var data = wrt_pinterface_namespace.ToByteArray().Concat(UTF8Encoding.UTF8.GetBytes(sig)).ToArray();
#else
            var maxBytes = UTF8Encoding.UTF8.GetMaxByteCount(sig.Length);

            var data = new byte[16 /* Number of bytes in a GUID */ + maxBytes];
            Span<byte> dataSpan = data;
            wrt_pinterface_namespace.TryWriteBytes(dataSpan);
            var numBytes = UTF8Encoding.UTF8.GetBytes(sig, dataSpan[16..]);
            data = data[..(16 + numBytes)];
#endif
            using (SHA1 sha = new SHA1CryptoServiceProvider())
            {
                return encode_guid(sha.ComputeHash(data));
            }
        }
    }
}
