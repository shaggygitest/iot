﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Device.Gpio;
using Windows.Devices.Enumeration;
using WinSpi = Windows.Devices.Spi;

namespace System.Device.Spi
{
    /// <summary>
    /// Represents a SPI communication channel running on Windows 10 IoT.
    /// </summary>
    internal class Windows10SpiDevice : SpiDevice
    {
        private readonly SpiConnectionSettings _settings;
        private WinSpi.SpiDevice _winDevice;
        private bool _isInverted = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="Windows10SpiDevice"/> class that will use the specified settings to communicate with the SPI device.
        /// </summary>
        /// <param name="settings">
        /// The connection settings of a device on a SPI bus.
        /// </param>
        public Windows10SpiDevice(SpiConnectionSettings settings)
        {
            if (settings.ChipSelectLineActiveState != PinValue.Low)
            {
                throw new PlatformNotSupportedException($"Changing{nameof(settings.ChipSelectLineActiveState)} options is not supported on the current platform.");
            }

            if (settings.DataFlow == DataFlow.LsbFirst)
            {
                _isInverted = true;
            }

            _settings = settings;

            // -1 means ignore Chip Select Line
            int chipSelectLine = _settings.ChipSelectLine == -1 ? 0 : _settings.ChipSelectLine;

            var winSettings = new WinSpi.SpiConnectionSettings(chipSelectLine)
            {
                Mode = ToWinMode(settings.Mode),
                DataBitLength = settings.DataBitLength,
                ClockFrequency = settings.ClockFrequency,
            };

            string busFriendlyName = $"SPI{settings.BusId}";
            string deviceSelector = WinSpi.SpiDevice.GetDeviceSelector(busFriendlyName);

            DeviceInformationCollection? deviceInformationCollection = DeviceInformation.FindAllAsync(deviceSelector).WaitForCompletion();
            if (deviceInformationCollection is null || deviceInformationCollection.Count == 0)
            {
                throw new ArgumentException($"No SPI device exists for bus ID {settings.BusId}.", nameof(settings));
            }

            WinSpi.SpiDevice? winDevice = WinSpi.SpiDevice.FromIdAsync(deviceInformationCollection[0].Id, winSettings).WaitForCompletion();

            if (winDevice is null)
            {
                throw new Exception("A SPI device could not be found.");
            }

            _winDevice = winDevice;
        }

        /// <summary>
        /// The connection settings of a device on a SPI bus. The connection settings are immutable after the device is created
        /// so the object returned will be a clone of the settings object.
        /// </summary>
        public override SpiConnectionSettings ConnectionSettings => new SpiConnectionSettings(_settings);

        /// <summary>
        /// Reads a byte from the SPI device.
        /// </summary>
        /// <returns>A byte read from the SPI device.</returns>
        public override byte ReadByte()
        {
            byte[] buffer = new byte[1];
            _winDevice.Read(buffer);
            return _isInverted ? ReverseByte(buffer[0]) : buffer[0];
        }

        /// <summary>
        /// Reads data from the SPI device.
        /// </summary>
        /// <param name="buffer">
        /// The buffer to read the data from the SPI device.
        /// The length of the buffer determines how much data to read from the SPI device.
        /// </param>
        public override void Read(Span<byte> buffer)
        {
            if (buffer.Length == 0)
            {
                throw new ArgumentException($"{nameof(buffer)} cannot be empty.");
            }

            byte[] byteArray = new byte[buffer.Length];
            _winDevice.Read(byteArray);
            new Span<byte>(byteArray).CopyTo(buffer);

            if (_isInverted)
            {
                ReverseByte(buffer);
            }
        }

        /// <summary>
        /// Writes a byte to the SPI device.
        /// </summary>
        /// <param name="value">The byte to be written to the SPI device.</param>
        public override void WriteByte(byte value)
        {
            _winDevice.Write(new[] { _isInverted ? ReverseByte(value) : value });
        }

        /// <summary>
        /// Writes data to the SPI device.
        /// </summary>
        /// <param name="buffer">
        /// The buffer that contains the data to be written to the SPI device.
        /// </param>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            byte[] toSend = buffer.ToArray();
            if (_isInverted)
            {
                ReverseByte(toSend);
            }

            _winDevice.Write(toSend);
        }

        /// <summary>
        /// Writes and reads data from the SPI device.
        /// </summary>
        /// <param name="writeBuffer">The buffer that contains the data to be written to the SPI device.</param>
        /// <param name="readBuffer">The buffer to read the data from the SPI device.</param>
        public override void TransferFullDuplex(ReadOnlySpan<byte> writeBuffer, Span<byte> readBuffer)
        {
            if (writeBuffer.Length != readBuffer.Length)
            {
                throw new ArgumentException($"Parameters '{nameof(writeBuffer)}' and '{nameof(readBuffer)}' must have the same length.");
            }

            byte[] readArray = new byte[readBuffer.Length];
            byte[] writeArray = writeBuffer.ToArray();

            if (_isInverted)
            {
                ReverseByte(writeArray);
            }

            _winDevice.TransferFullDuplex(writeArray, readArray);
            readArray.CopyTo(readBuffer);
            if (_isInverted)
            {
                ReverseByte(readBuffer);
            }
        }

        protected override void Dispose(bool disposing)
        {
            _winDevice?.Dispose();
            _winDevice = null!;

            base.Dispose(disposing);
        }

        private static WinSpi.SpiMode ToWinMode(SpiMode mode)
        {
            return mode switch
            {
                SpiMode.Mode0 => WinSpi.SpiMode.Mode0,
                SpiMode.Mode1 => WinSpi.SpiMode.Mode1,
                SpiMode.Mode2 => WinSpi.SpiMode.Mode2,
                SpiMode.Mode3 => WinSpi.SpiMode.Mode3,
                _ => throw new ArgumentException($"SPI mode {mode} not supported.", nameof(mode))
            };
        }
    }
}
