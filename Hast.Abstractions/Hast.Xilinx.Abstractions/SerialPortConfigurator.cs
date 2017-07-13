﻿using System.IO.Ports;
using Hast.Common.Extensibility.Pipeline;
using Hast.Communication.Extensibility.Pipeline;
using Hast.Communication.Models;

namespace Hast.Xilinx.Abstractions
{
    public class SerialPortConfigurator : PipelineStepBase, ISerialPortConfigurator
    {
        public void ConfigureSerialPort(SerialPort serialPort, IHardwareExecutionContext hardwareExecutionContext)
        {
            if (hardwareExecutionContext.HardwareRepresentation.DeviceManifest.Name != Nexys4DdrManifestProvider.DeviceName) return;

            serialPort.BaudRate = 230400;
        }
    }
}
