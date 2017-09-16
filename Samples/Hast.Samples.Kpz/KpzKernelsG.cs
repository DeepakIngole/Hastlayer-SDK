﻿
using System.Threading.Tasks;
using Hast.Transformer.Abstractions.SimpleMemory;
using System;

namespace Hast.Samples.Kpz
{
    public class KpzKernelsIndexObject
    {
        public bool[] bramDx;
        public bool[] bramDy;
        public ulong taskRandomState1;
        public ulong taskRandomState2;
    }

    public class KpzKernelsGInterface
    {
        const uint integerProbabilityP = 32767, integerProbabilityQ = 32767;
        //These parameters are fixed, locked into VHDL code for simplicity
        public const int GridSize = 64; //Full grid width and height
        //Local grid width and height (GridSize^2)/(LocalGridSize^2) need to be an integer for simplicity
        public const int LocalGridSize = 8;
        public const int ParallelTasks = 8; //Number of parallel execution engines
        public const int NumberOfIterations = 10;

        //public int MemStartOfRandomValues() { return GridSize * GridSize;  }
        //public int MemStartOfParameters() { return GridSize * GridSize + TasksPerIteration * NumberOfIterations + 1; }

        public virtual void ScheduleIterations(SimpleMemory memory)
        {
            const int TasksPerIteration = (GridSize * GridSize) / (LocalGridSize * LocalGridSize);
            const int SchedulesPerIteration = TasksPerIteration / ParallelTasks;
            const float IterationsPerTask = 0.5F;
            const int IterationGroupSize = (int)(NumberOfIterations / IterationsPerTask);
            const int PokesInsideTask = (int)(LocalGridSize * LocalGridSize * IterationsPerTask);
            const int LocalGridPartitions = GridSize / LocalGridSize;
            //const int TotalNumberOfTasks = TasksPerIteration * NumberOfIterations == ((GridSize * GridSize) / (LocalGridSize * LocalGridSize)) * NumberOfIterations
            ulong randomState0;
            int ParallelTaskRandomIndex = 0;
            uint RandomSeedTemp;

            KpzKernelsIndexObject[] TaskLocals = new KpzKernelsIndexObject[ParallelTasks];
            for (int TaskLocalsIndex = 0; TaskLocalsIndex < ParallelTasks; TaskLocalsIndex++)
            {
                TaskLocals[TaskLocalsIndex] = new KpzKernelsIndexObject();
                TaskLocals[TaskLocalsIndex].bramDx = new bool[LocalGridSize * LocalGridSize];
                TaskLocals[TaskLocalsIndex].bramDy = new bool[LocalGridSize * LocalGridSize];
                TaskLocals[TaskLocalsIndex].taskRandomState1 = memory.ReadUInt32(GridSize * GridSize + ParallelTaskRandomIndex++);
                RandomSeedTemp = memory.ReadUInt32(GridSize * GridSize + ParallelTaskRandomIndex++);
                TaskLocals[TaskLocalsIndex].taskRandomState1 |= ((ulong)RandomSeedTemp) << 32;

                TaskLocals[TaskLocalsIndex].taskRandomState2 = memory.ReadUInt32(GridSize * GridSize + ParallelTaskRandomIndex++);
                RandomSeedTemp = memory.ReadUInt32(GridSize * GridSize + ParallelTaskRandomIndex++);
                TaskLocals[TaskLocalsIndex].taskRandomState2 |= ((ulong)RandomSeedTemp) << 32;
            }

            //What is IterationGroupIndex good for?
            //IterationPerTask needs to be between 0.5 and 1 based on the e-mail of Mate.
            //If we want 10 iterations, and starting a full series of tasks makes half iteration on the full table,
            //then we need to start it 20 times (thus IterationGroupSize will be 20).


            randomState0 = memory.ReadUInt32(GridSize * GridSize + ParallelTaskRandomIndex++);
            RandomSeedTemp = memory.ReadUInt32(GridSize * GridSize + ParallelTaskRandomIndex++);
            randomState0 |= ((ulong)RandomSeedTemp) << 32;

            for (int IterationGroupIndex = 0; IterationGroupIndex < IterationGroupSize; IterationGroupIndex++)
            {
                //GetNextRandom0
                uint c0 = (uint)(randomState0 >> 32);
                ulong x0l = randomState0 & (0xFFFFFFFFUL);
                uint x0 = (uint)x0l;
                // Creating the value 0xFFFEB81BUL. This literal can't be directly used due to an ILSpy bug, see:
                // https://github.com/icsharpcode/ILSpy/issues/807
                uint z01 = 0xFFFE;
                uint z02 = 0xB81B;
                uint z0 = (0 << 32) | (z01 << 16) | z02;
                randomState0 = x0 * z0 + c0;
                uint RandomValue0 = x0 ^ c0;
                int RandomXOffset = (int)((LocalGridSize - 1) & RandomValue0); //This supposes that LocalGridSize is 2^N
                int RandomYOffset = (int)((LocalGridSize - 1) & (RandomValue0>>16));
                for (int ScheduleIndex = 0; ScheduleIndex < SchedulesPerIteration; ScheduleIndex++)
                {
                    var tasks = new Task<KpzKernelsIndexObject>[ParallelTasks];
                    for (int ParallelTaskIndex = 0; ParallelTaskIndex < ParallelTasks; ParallelTaskIndex++)
                    {
                        //Decide the X and Y starting coordinates based on ScheduleIndex and ParallelTaskIndex (and the random added value)
                        int LocalGridIndex = ParallelTaskIndex + ScheduleIndex * ParallelTasks;
                        int PartitionX = LocalGridIndex % LocalGridPartitions; //The X and Y coordinate within the small table (local grid)
                        int PartitionY = LocalGridIndex / LocalGridPartitions;
                        int BaseX = PartitionX * LocalGridSize + RandomXOffset; //The X and Y coordinate within the big table (grid)
                        int BaseY = PartitionY * LocalGridSize + RandomYOffset;

                        //Copy to local memory
                        for (int CopyDstX = 0; CopyDstX < LocalGridSize; CopyDstX++)
                        {
                            for (int CopyDstY = 0; CopyDstY < LocalGridSize; CopyDstY++)
                            {
                                int CopySrcX = (BaseX + CopyDstX) % GridSize;
                                int CopySrcY = ((BaseY + CopyDstY) / GridSize) % GridSize; //Prevent going out of grid memory area (e.g. reading into random seed)
                                uint value = memory.ReadUInt32(CopySrcX + CopySrcY * GridSize);
                                TaskLocals[ParallelTaskIndex].bramDx[CopyDstX + CopyDstY * LocalGridSize] = (value & 1) == 1;
                                TaskLocals[ParallelTaskIndex].bramDy[CopyDstX + CopyDstY * LocalGridSize] = (value & 2) == 2;
                            }
                        }

                        tasks[ParallelTaskIndex] = Task.Factory.StartNew(
                        rawIndexObject =>
                        {
                            //Then do TasksPerIteration iterations
                            KpzKernelsIndexObject TaskLocal = (KpzKernelsIndexObject)rawIndexObject;
                            for (int PokeIndex = 0; PokeIndex < PokesInsideTask; PokeIndex++)
                            {
                                // ==== <Now randomly switch four cells> ====

                                //Generating two random numbers:

                                //GetNextRandom1
                                uint c1 = (uint)(TaskLocal.taskRandomState1 >> 32);
                                uint x1 = (uint)(TaskLocal.taskRandomState1 & 0xFFFFFFFFUL);
                                // Creating the value 0xFFFEB81BUL. This literal can't be directly used due to an ILSpy bug, see:
                                // https://github.com/icsharpcode/ILSpy/issues/807
                                uint z11 = 0xFFFE;
                                uint z12 = 0xB81B;
                                uint z1 = (0 << 32) | (z11 << 16) | z12;
                                TaskLocal.taskRandomState1 = x1 * z1 + c1;
                                uint taskRandomNumber1 = x1 ^ c1;

                                //GetNextRandom2
                                uint c2 = (uint)(TaskLocal.taskRandomState2 >> 32);
                                uint x2 = (uint)(TaskLocal.taskRandomState2 & 0xFFFFFFFFUL);
                                // Creating the value 0xFFFEB81BUL. This literal can't be directly used due to an ILSpy bug, see:
                                // https://github.com/icsharpcode/ILSpy/issues/807
                                uint z21 = 0xFFFE;
                                uint z22 = 0xB81B;
                                uint z2 = (0 << 32) | (z21 << 16) | z22;
                                TaskLocal.taskRandomState2 = x2 * z2 + c2;
                                uint taskRandomNumber2 = x2 ^ c2;

                                int pokeCenterX = (int)(taskRandomNumber1 & (LocalGridSize - 1));
                                int pokeCenterY = (int)((taskRandomNumber1 >> 16) & (LocalGridSize - 1));
                                int pokeCenterIndex = pokeCenterX + pokeCenterY * LocalGridSize;
                                uint randomVariable1 = taskRandomNumber2 & ((1 << 16) - 1);
                                uint randomVariable2 = (taskRandomNumber2 >> 16) & ((1 << 16) - 1);

                                int rightNeighbourIndex;
                                int bottomNeighbourIndex;
                                //get neighbour indexes:
                                if (pokeCenterX >= LocalGridSize - 1 || pokeCenterY >= LocalGridSize - 1) continue; //We skip if neighbours would fall out of the local grid
                                int rightNeighbourX = pokeCenterX + 1;
                                int rightNeighbourY = pokeCenterY;
                                int bottomNeighbourX = pokeCenterX;
                                int bottomNeighbourY = pokeCenterY + 1;
                                rightNeighbourIndex = rightNeighbourY * LocalGridSize + rightNeighbourX;
                                bottomNeighbourIndex = bottomNeighbourY * LocalGridSize + bottomNeighbourX;

                                // We check our own {dx,dy} values, and the right neighbour's dx, and bottom neighbour's dx.
                                if (
                                    // If we get the pattern {01, 01} we have a pyramid:
                                    ((TaskLocal.bramDx[pokeCenterIndex] && !TaskLocal.bramDx[rightNeighbourIndex]) &&
                                    (TaskLocal.bramDy[pokeCenterIndex] && !TaskLocal.bramDy[bottomNeighbourIndex]) &&
                                    (randomVariable1 < integerProbabilityP)) ||
                                    // If we get the pattern {10, 10} we have a hole:
                                    ((!TaskLocal.bramDx[pokeCenterIndex] && TaskLocal.bramDx[rightNeighbourIndex]) &&
                                    (!TaskLocal.bramDy[pokeCenterIndex] && TaskLocal.bramDy[bottomNeighbourIndex]) &&
                                    (randomVariable2 < integerProbabilityQ))
                                )
                                {
                                    // We make a hole into a pyramid, and a pyramid into a hole.
                                    TaskLocal.bramDx[pokeCenterIndex] = !TaskLocal.bramDx[pokeCenterIndex];
                                    TaskLocal.bramDy[pokeCenterIndex] = !TaskLocal.bramDy[pokeCenterIndex];
                                    TaskLocal.bramDx[rightNeighbourIndex] = !TaskLocal.bramDx[rightNeighbourIndex];
                                    TaskLocal.bramDy[bottomNeighbourIndex] = !TaskLocal.bramDy[bottomNeighbourIndex];
                                }
                                // ==== </Now randomly switch four cells> ====
                            }
                            return TaskLocal; //TODO: do we need this at all?
                        }, TaskLocals[ParallelTaskIndex]);
                    }

                    Task.WhenAll(tasks).Wait();

                    //Copy back to SimpleMemory
                    for (int ParallelTaskIndex = 0; ParallelTaskIndex < ParallelTasks; ParallelTaskIndex++)
                    {
                        //calculate these things again
                        int LocalGridIndex = ParallelTaskIndex + ScheduleIndex * ParallelTasks;
                        int PartitionX = LocalGridIndex % LocalGridPartitions; //The X and Y coordinate within the small table (local grid)
                        int PartitionY = LocalGridIndex / LocalGridPartitions;
                        int BaseX = PartitionX * LocalGridSize + RandomXOffset; //The X and Y coordinate within the big table (grid)
                        int BaseY = PartitionY * LocalGridSize + RandomYOffset;

                        for (int CopyDstX = 0; CopyDstX < LocalGridSize; CopyDstX++)
                        {
                            for (int CopyDstY = 0; CopyDstY < LocalGridSize; CopyDstY++)
                            {
                                int CopySrcX = (BaseX + CopyDstX) % GridSize;
                                int CopySrcY = ((BaseY + CopyDstY) / GridSize) % GridSize;
                                uint value =
                                    (TaskLocals[ParallelTaskIndex].bramDx[CopyDstX + CopyDstY * LocalGridSize] ? 1U : 0U) |
                                    (TaskLocals[ParallelTaskIndex].bramDy[CopyDstX + CopyDstY * LocalGridSize] ? 2U : 0U);
                                memory.WriteUInt32(CopySrcX + CopySrcY * GridSize, value);
                            }
                        }
                    }
                }
            }
        }
    }

    public static class KpzKernelsGExtensions
    {
        public static void CopyTo(this KpzKernelsGInterface kernels, SimpleMemory memoryDst, KpzNode[,] gridSrc)
        {
            for (int x = 0; x < KpzKernels.GridHeight; x++)
            {
                for (int y = 0; y < KpzKernelsGInterface.GridSize; y++)
                {
                    KpzNode node = gridSrc[x, y];
                    memoryDst.WriteUInt32(y * KpzKernelsGInterface.GridSize + x, node.SerializeToUInt32());
                }
            }

            uint NumberOfRandomSeedValues = ((KpzKernelsGInterface.GridSize * KpzKernelsGInterface.GridSize) / (KpzKernelsGInterface.LocalGridSize * KpzKernelsGInterface.LocalGridSize) * KpzKernelsGInterface.NumberOfIterations + 1) * 2;
            Random random = new Random();
            for (int RandomSeedCopyIndex = 0; RandomSeedCopyIndex < NumberOfRandomSeedValues; RandomSeedCopyIndex++)
            {
                uint randomNumber = (uint)random.Next();
                memoryDst.WriteUInt32(KpzKernelsGInterface.GridSize * KpzKernelsGInterface.GridSize + RandomSeedCopyIndex, randomNumber);
            }
        }

        public static void DoIterationsWrapper(this KpzKernelsGInterface kernels, KpzNode[,] hostGrid, bool pushToFpga)
        {
            int numTasks = ((KpzKernelsGInterface.GridSize * KpzKernelsGInterface.GridSize) / (KpzKernelsGInterface.LocalGridSize * KpzKernelsGInterface.LocalGridSize)) * KpzKernelsGInterface.NumberOfIterations;
            int numRandomUints = 2 + (numTasks * 4);
            SimpleMemory sm = new SimpleMemory(KpzKernelsGInterface.GridSize * KpzKernelsGInterface.GridSize + numRandomUints);

            if (pushToFpga) KpzKernelsGExtensions.CopyFromGridToSimpleMemory(hostGrid, sm);

            Random rnd = new Random();
            for (int randomWriteIndex=0; randomWriteIndex<numTasks; randomWriteIndex++)
                sm.WriteUInt32(KpzKernelsGInterface.GridSize * KpzKernelsGInterface.GridSize + randomWriteIndex, (uint)rnd.Next());

            kernels.ScheduleIterations(sm);

            KpzKernelsGExtensions.CopyFromSimpleMemoryToGrid(hostGrid, sm);
        }

       /// <summary>Push table into FPGA.</summary>
        public static void CopyFromGridToSimpleMemory(KpzNode[,] gridSrc, SimpleMemory memoryDst)
        {
            for (int x = 0; x < KpzKernelsGInterface.GridSize; x++)
            {
                for (int y = 0; y < KpzKernelsGInterface.GridSize; y++)
                {
                    KpzNode node = gridSrc[x, y];
                    memoryDst.WriteUInt32(y * KpzKernels.GridWidth + x, node.SerializeToUInt32());
                }
            }
        }

        /// <summary>Pull table from the FPGA.</summary>
        public static void CopyFromSimpleMemoryToGrid(KpzNode[,] gridDst, SimpleMemory memorySrc)
        {
            for (int x = 0; x < KpzKernelsGInterface.GridSize; x++)
            {
                for (int y = 0; y < KpzKernelsGInterface.GridSize; y++)
                {
                    gridDst[x, y] = KpzNode.DeserializeFromUInt32(memorySrc.ReadUInt32(y * KpzKernels.GridWidth + x));
                }
            }
        }
    }
}
