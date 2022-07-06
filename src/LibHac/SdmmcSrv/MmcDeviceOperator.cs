using LibHac.Common;
using LibHac.Diag;
using LibHac.Fs;
using LibHac.FsSrv.Storage.Sf;
using LibHac.FsSystem;
using LibHac.Os;
using LibHac.Sdmmc;
using LibHac.Sf;
using static LibHac.Sdmmc.SdmmcApi;
using static LibHac.SdmmcSrv.SdmmcResultConverter;
using MmcPartition = LibHac.Sdmmc.MmcPartition;

namespace LibHac.SdmmcSrv;

internal class MmcDeviceOperator : IStorageDeviceOperator
{
    private SharedRef<MmcPartitionStorageDevice> _storageDevice;

    // LibHac additions
    private readonly SdmmcApi _sdmmc;

    public MmcDeviceOperator(ref SharedRef<MmcPartitionStorageDevice> storageDevice, SdmmcApi sdmmc)
    {
        _storageDevice = SharedRef<MmcPartitionStorageDevice>.CreateMove(ref storageDevice);
        _sdmmc = sdmmc;
    }

    public void Dispose()
    {
        _storageDevice.Destroy();
    }

    public Result Operate(int operationId)
    {
        var operation = (MmcOperationIdValue)operationId;

        using var scopedLock = new UniqueLockRef<SdkMutexType>();
        Result rc = _storageDevice.Get.Lock(ref scopedLock.Ref());
        if (rc.IsFailure()) return rc.Miss();

        switch (operation)
        {
            case MmcOperationIdValue.Erase:
            {
                Port port = _storageDevice.Get.GetPort();
                MmcPartition partition = _storageDevice.Get.GetPartition();

                Abort.DoAbortUnlessSuccess(_sdmmc.SelectMmcPartition(port, partition));

                try
                {
                    rc = GetFsResult(port, _sdmmc.EraseMmc(port));
                    if (rc.IsFailure()) return rc.Miss();

                    return Result.Success;
                }
                finally
                {
                    Abort.DoAbortUnlessSuccess(_sdmmc.SelectMmcPartition(port, MmcPartition.UserData));
                }
            }
            default:
                return ResultFs.InvalidArgument.Log();
        }
    }

    public Result OperateIn(InBuffer buffer, long offset, long size, int operationId)
    {
        return ResultFs.NotImplemented.Log();
    }

    public Result OperateOut(out long bytesWritten, OutBuffer buffer, int operationId)
    {
        bytesWritten = 0;
        var operation = (MmcOperationIdValue)operationId;

        using var scopedLock = new UniqueLockRef<SdkMutexType>();
        Result rc = _storageDevice.Get.Lock(ref scopedLock.Ref());
        if (rc.IsFailure()) return rc.Miss();

        Port port = _storageDevice.Get.GetPort();

        switch (operation)
        {
            case MmcOperationIdValue.GetSpeedMode:
            {
                if (buffer.Size < sizeof(SpeedMode))
                    return ResultFs.InvalidArgument.Log();

                rc = GetFsResult(port, _sdmmc.GetDeviceSpeedMode(out buffer.As<SpeedMode>(), port));
                if (rc.IsFailure()) return rc.Miss();

                bytesWritten = sizeof(SpeedMode);
                return Result.Success;
            }
            case MmcOperationIdValue.GetCid:
            {
                if (buffer.Size < DeviceCidSize)
                    return ResultFs.InvalidSize.Log();

                rc = GetFsResult(port, _sdmmc.GetDeviceCid(buffer.Buffer.Slice(0, DeviceCidSize), port));
                if (rc.IsFailure()) return rc.Miss();

                bytesWritten = DeviceCidSize;
                return Result.Success;
            }
            case MmcOperationIdValue.GetPartitionSize:
            {
                if (buffer.Size < sizeof(long))
                    return ResultFs.InvalidArgument.Log();

                rc = GetFsResult(port, GetPartitionCapacity(out uint numSectors, port));
                if (rc.IsFailure()) return rc.Miss();

                buffer.As<long>() = numSectors * SectorSize;
                bytesWritten = sizeof(long);

                return Result.Success;
            }
            case MmcOperationIdValue.GetExtendedCsd:
            {
                if (buffer.Size < MmcExtendedCsdSize)
                    return ResultFs.InvalidSize.Log();

                using var pooledBuffer = new PooledBuffer(MmcExtendedCsdSize, MmcExtendedCsdSize);
                if (pooledBuffer.GetSize() < MmcExtendedCsdSize)
                    return ResultFs.AllocationMemoryFailedInSdmmcStorageServiceB.Log();

                rc = GetFsResult(port, _sdmmc.GetMmcExtendedCsd(pooledBuffer.GetBuffer(), port));
                if (rc.IsFailure()) return rc.Miss();

                pooledBuffer.GetBuffer().Slice(0, MmcExtendedCsdSize).CopyTo(buffer.Buffer);
                bytesWritten = MmcExtendedCsdSize;

                return Result.Success;
            }
            default:
                return ResultFs.InvalidArgument.Log();
        }
    }

    public Result OperateOut2(out long bytesWrittenBuffer1, OutBuffer buffer1, out long bytesWrittenBuffer2,
        OutBuffer buffer2, int operationId)
    {
        UnsafeHelpers.SkipParamInit(out bytesWrittenBuffer1, out bytesWrittenBuffer2);

        return ResultFs.NotImplemented.Log();
    }

    public Result OperateInOut(out long bytesWritten, OutBuffer outBuffer, InBuffer inBuffer, long offset, long size,
        int operationId)
    {
        UnsafeHelpers.SkipParamInit(out bytesWritten);

        return ResultFs.NotImplemented.Log();
    }

    public Result OperateIn2Out(out long bytesWritten, OutBuffer outBuffer, InBuffer inBuffer1, InBuffer inBuffer2,
        long offset, long size, int operationId)
    {
        UnsafeHelpers.SkipParamInit(out bytesWritten);

        return ResultFs.NotImplemented.Log();
    }

    private Result GetPartitionCapacity(out uint outNumSectors, Port port)
    {
        UnsafeHelpers.SkipParamInit(out outNumSectors);

        switch (_storageDevice.Get.GetPartition())
        {
            case MmcPartition.UserData:
            {
                Result rc = _sdmmc.GetDeviceMemoryCapacity(out outNumSectors, port);
                if (rc.IsFailure()) return rc.Miss();

                return Result.Success;
            }
            case MmcPartition.BootPartition1:
            case MmcPartition.BootPartition2:
            {
                Result rc = _sdmmc.GetMmcBootPartitionCapacity(out outNumSectors, port);
                if (rc.IsFailure()) return rc.Miss();

                return Result.Success;
            }
            default:
                return ResultFs.InvalidArgument.Log();
        }
    }
}