﻿using Hast.Layer;
using System;
using System.IO;
using System.Threading.Tasks;
using Hast.Transformer.Vhdl.Abstractions.Configuration;
using System.Linq;
using Hast.Transformer.Abstractions.SimpleMemory;

namespace Hast.Samples.Kpz
{
    public partial class Kpz
    {
        public string VhdlOutputFilePath = @"Hast_IP.vhd";
        public delegate void LogItDelegate(string toLog);
        public LogItDelegate LogItFunction; //Should be AsyncLogIt from ChartForm
        public KpzKernelsInterface Kernels;
        public KpzKernelsGInterface KernelsG;
        public PrngTestInterface KernelsP;
        private bool _verifyOutput;

        public async Task InitializeHastlayer(bool verifyOutput)
        {
            _verifyOutput = verifyOutput;

            LogItFunction("Creating Hastlayer Factory...");
            var hastlayer = await Hastlayer.Create();
            hastlayer.ExecutedOnHardware += (sender, e) =>
            {
                LogItFunction("Hastlayer timer: " +
                    e.HardwareExecutionInformation.HardwareExecutionTimeMilliseconds + "ms (net) / " +
                    e.HardwareExecutionInformation.FullExecutionTimeMilliseconds + " ms (total)"
                );
            };

            var configuration = new HardwareGenerationConfiguration((await hastlayer.GetSupportedDevices()).First().Name);
            configuration.VhdlTransformerConfiguration().VhdlGenerationMode = VhdlGenerationMode.Debug;
            configuration.EnableCaching = false;

            LogItFunction("Generating hardware...");
            IHardwareRepresentation hardwareRepresentation;
            if (kpzTarget.HastlayerGAlgorithm())
            {
                configuration.HardwareEntryPointMemberNamePrefixes.Add("Hast.Samples.Kpz.KpzKernelsGInterface");
                hardwareRepresentation = await hastlayer.GenerateHardware(new[] {
                    typeof(KpzKernelsGInterface).Assembly,
                 //   typeof(Hast.Algorithms.MWC64X).Assembly
                }, configuration);
            }
            else if (kpzTarget.HastlayerPlainAlgorithm())
            {
                configuration.HardwareEntryPointMemberNamePrefixes.Add("Hast.Samples.Kpz.KpzKernelsInterface");
                hardwareRepresentation = await hastlayer.GenerateHardware(new[] {
                    typeof(KpzKernelsInterface).Assembly
                }, configuration);
            }
            else // if (kpzTarget == KpzTarget.PrngTest)
            {
                configuration.HardwareEntryPointMemberNamePrefixes.Add("Hast.Samples.Kpz.PrngTestInterface");
                hardwareRepresentation = await hastlayer.GenerateHardware(new[] {
                    typeof(PrngTestInterface).Assembly
                }, configuration);
            }

            await hardwareRepresentation.HardwareDescription.WriteSource(VhdlOutputFilePath);

            LogItFunction("Generating proxy...");
            if (kpzTarget.HastlayerOnFpga())
            {
                ProxyGenerationConfiguration proxyConf = new ProxyGenerationConfiguration();
                proxyConf.VerifyHardwareResults = _verifyOutput;
                if(kpzTarget == KpzTarget.Fpga)
                {
                    Kernels = await hastlayer.GenerateProxy<KpzKernelsInterface>(
                        hardwareRepresentation, 
                        new KpzKernelsInterface(), 
                        proxyConf);
                }
                else if(kpzTarget == KpzTarget.FpgaG) 
                {
                    KernelsG = await hastlayer.GenerateProxy<KpzKernelsGInterface>(
                        hardwareRepresentation, 
                        new KpzKernelsGInterface(), 
                        proxyConf);
                }
                else //if(kpzTarget == KpzTarget.PrngTest) 
                {
                    KernelsP = await hastlayer.GenerateProxy<PrngTestInterface>(
                        hardwareRepresentation, 
                        new PrngTestInterface(), 
                        proxyConf);
                }
                LogItFunction("FPGA target detected");
            }
            else //if (kpzTarget == KpzTarget.HastlayerSimulation())
            {
                Kernels = new KpzKernelsInterface();
                KernelsG = new KpzKernelsGInterface();
                LogItFunction("Simulation target detected");
            }

            if(kpzTarget.HastlayerPlainAlgorithm())
            {
                LogItFunction("Running TestAdd...");
                uint resultFpga = Kernels.TestAddWrapper(4313,123);
                uint resultCpu  = 4313+123;
                if(resultCpu == resultFpga) LogItFunction(String.Format("Success: {0} == {1}", resultFpga, resultCpu));
                else LogItFunction(String.Format("Fail: {0} != {1}", resultFpga, resultCpu));
            }

            if (kpzTarget == KpzTarget.PrngTest)
            {
                LogItFunction("Running TestPrng...");

                PrngTestInterface KernelsCpu = new PrngTestInterface();
                ulong randomSeed = 0x37a92d76a96ef210UL;
                SimpleMemory smCpu = KernelsCpu.PushRandomSeed(randomSeed);
                SimpleMemory smFpga = KernelsP.PushRandomSeed(randomSeed);
                LogItFunction("PRNG results:");
                bool success = true;
                for (int PrngTestIndex = 0; PrngTestIndex < 10; PrngTestIndex++)
                {
                    uint prngCpuResult = KernelsCpu.GetNextRandom(smCpu);
                    uint prngFpgaResult = KernelsP.GetNextRandom(smFpga);
                    if (prngCpuResult != prngFpgaResult) { success = false; }
                    LogItFunction(String.Format("{0}, {1}", prngCpuResult, prngFpgaResult));
                }
                if(success) LogItFunction("TestPrng succeeded!");
                else LogItFunction("TestPrng failed!");
            }
        }
    }
}
