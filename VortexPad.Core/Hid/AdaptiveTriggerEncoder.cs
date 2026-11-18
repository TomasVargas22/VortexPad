using VortexPad.Core.Models;
using VortexPad.Core.Profiles;

namespace VortexPad.Core.Hid
{
    /// <summary>
    /// Validated adaptive trigger effect, as sent to the controller. A value type (value equality),
    /// so the reader only resends when it changes.
    /// </summary>
    public readonly record struct AdaptiveTriggerEffect(
        AdaptiveTriggerMode Mode,
        byte StartPosition = 0,
        byte EndPosition = 0,
        byte Strength = 0,
        byte FrequencyHz = 0)
    {
        public static AdaptiveTriggerEffect Off => default;

        /// <summary>Converts the profile settings, clamping each parameter to its valid range.</summary>
        public static AdaptiveTriggerEffect FromConfig(AdaptiveTriggerConfiguration? config)
        {
            if (config == null || config.Mode == AdaptiveTriggerMode.Off) return Off;

            byte strength = (byte)Math.Clamp(config.Strength, 1, 8);
            return config.Mode switch
            {
                AdaptiveTriggerMode.Resistance => new(config.Mode, (byte)Math.Clamp(config.StartPosition, 0, 9), 0, strength),
                AdaptiveTriggerMode.Weapon => FromWeapon(config, strength),
                AdaptiveTriggerMode.Vibration => new(config.Mode, (byte)Math.Clamp(config.StartPosition, 0, 9), 0, strength,
                    (byte)Math.Clamp(config.FrequencyHz, 1, 255)),
                _ => Off
            };
        }

        private static AdaptiveTriggerEffect FromWeapon(AdaptiveTriggerConfiguration config, byte strength)
        {
            // The firmware accepts start 2–7 and end in (start, 8]
            byte start = (byte)Math.Clamp(config.StartPosition, 2, 7);
            byte end = (byte)Math.Clamp(config.EndPosition, start + 1, 8);
            return new(AdaptiveTriggerMode.Weapon, start, end, strength);
        }
    }

    /// <summary>
    /// Encodes the 11 bytes of a DualSense trigger effect (raw modes 0x05 off, 0x21 feedback,
    /// 0x25 weapon and 0x26 vibration, with 10 zones along the travel).
    /// </summary>
    public static class AdaptiveTriggerEncoder
    {
        public const int EffectLength = 11;

        private const byte ModeOff = 0x05;
        private const byte ModeFeedback = 0x21;
        private const byte ModeWeapon = 0x25;
        private const byte ModeVibration = 0x26;

        public static void Encode(in AdaptiveTriggerEffect effect, Span<byte> destination)
        {
            if (destination.Length < EffectLength)
                throw new ArgumentException($"Se necesitan {EffectLength} bytes.", nameof(destination));

            Span<byte> d = destination[..EffectLength];
            d.Clear();

            switch (effect.Mode)
            {
                case AdaptiveTriggerMode.Resistance:
                    WriteZoned(d, ModeFeedback, effect.StartPosition, effect.Strength);
                    break;

                case AdaptiveTriggerMode.Weapon:
                {
                    ushort startAndStop = (ushort)((1 << effect.StartPosition) | (1 << effect.EndPosition));
                    d[0] = ModeWeapon;
                    d[1] = (byte)(startAndStop & 0xFF);
                    d[2] = (byte)(startAndStop >> 8);
                    d[3] = (byte)((effect.Strength - 1) & 0x07);
                    break;
                }

                case AdaptiveTriggerMode.Vibration:
                    WriteZoned(d, ModeVibration, effect.StartPosition, effect.Strength);
                    d[9] = effect.FrequencyHz;
                    break;

                default:
                    d[0] = ModeOff;
                    break;
            }
        }

        /// <summary>
        /// Zone layout: bytes 1–2 = active zones (10 bits, one per position from the start),
        /// bytes 3–6 = 3-bit strength per zone (30 bits).
        /// </summary>
        private static void WriteZoned(Span<byte> d, byte mode, byte startPosition, byte strength)
        {
            uint zoneStrength = (uint)((strength - 1) & 0x07);
            uint strengthZones = 0;
            ushort activeZones = 0;
            for (int i = Math.Min((int)startPosition, 9); i < 10; i++)
            {
                strengthZones |= zoneStrength << (3 * i);
                activeZones |= (ushort)(1 << i);
            }

            d[0] = mode;
            d[1] = (byte)(activeZones & 0xFF);
            d[2] = (byte)(activeZones >> 8);
            d[3] = (byte)(strengthZones & 0xFF);
            d[4] = (byte)((strengthZones >> 8) & 0xFF);
            d[5] = (byte)((strengthZones >> 16) & 0xFF);
            d[6] = (byte)((strengthZones >> 24) & 0xFF);
        }
    }
}
