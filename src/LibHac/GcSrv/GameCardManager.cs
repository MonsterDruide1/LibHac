using System;
using System.Runtime.CompilerServices;
using LibHac.Common;
using LibHac.Diag;
using LibHac.Fs;
using LibHac.FsSrv;
using LibHac.FsSrv.Sf;
using LibHac.FsSrv.Storage.Sf;
using LibHac.FsSystem;
using LibHac.Gc;
using LibHac.Gc.Impl;
using LibHac.Gc.Writer;
using LibHac.Os;
using LibHac.Sf;
using IStorage = LibHac.FsSrv.Sf.IStorage;

namespace LibHac.GcSrv;

public class GameCardManager : IStorageDeviceManager, IStorageDeviceOperator, IGameCardDeviceManager
{
    private enum CardState
    {
        Initial = 0,
        Normal = 1,
        Secure = 2,
        Write = 3
    }

    private ReaderWriterLock _rwLock;
    private bool _isInitialized;
    private bool _isFinalized;
    private CardState _state;
    private GameCardHandle _currentHandle;
    private GameCardDeviceDetectionEventManager _detectionEventManager;

    // LibHac additions
    private WeakRef<GameCardManager> _selfReference;
    private readonly FileSystemServer _fsServer;
    private readonly GameCardDummy _gc;

    private GameCardManager(FileSystemServer fsServer)
    {
        _rwLock = new ReaderWriterLock(fsServer.Hos.Os);

        _fsServer = fsServer;
    }

    public static SharedRef<GameCardManager> CreateShared(FileSystemServer fsServer)
    {
        var manager = new GameCardManager(fsServer);

        using var sharedManager = new SharedRef<GameCardManager>(manager);
        manager._selfReference.Set(in sharedManager);

        return SharedRef<GameCardManager>.CreateMove(ref sharedManager.Ref());
    }

    public void Dispose()
    {
        _detectionEventManager?.Dispose();
        _detectionEventManager = null;

        _rwLock?.Dispose();
        _rwLock = null;
    }

    private uint BytesToPages(long byteCount)
    {
        return (uint)((ulong)byteCount / (ulong)Values.GcPageSize);
    }

    private void DeactivateAndChangeState()
    {
        _gc.Deactivate();
        _currentHandle++;
        _state = CardState.Initial;
    }

    private void CheckGameCardAndDeactivate()
    {
        if (_state != CardState.Initial && !_gc.IsCardActivationValid())
        {
            DeactivateAndChangeState();
        }
    }

    private Result ActivateGameCard()
    {
        Result rc = HandleGameCardAccessResult(_gc.Activate());
        if (rc.IsFailure()) return rc.Miss();

        return Result.Success;
    }

    private Result ActivateGameCardForWriter()
    {
        return HandleGameCardAccessResult(_gc.Writer.ActivateForWriter());
    }

    private Result SetGameCardToSecureMode()
    {
        return HandleGameCardAccessResult(_gc.SetCardToSecureMode());
    }

    public Result IsInserted(out bool isInserted)
    {
        UnsafeHelpers.SkipParamInit(out isInserted);

        Result rc = InitializeGcLibrary();
        if (rc.IsFailure()) return rc.Miss();

        isInserted = _gc.IsCardInserted();

        return Result.Success;
    }

    public Result InitializeGcLibrary()
    {
        using var writeLock = new UniqueLock<ReaderWriterLock>(_rwLock);

        if (_isFinalized)
            return ResultFs.PreconditionViolation.Log();

        if (_isInitialized)
            return Result.Success;

        // Missing: Wait on settings-ready event
        // Missing: Allocate work buffer and pass it to nn::gc::Initialize
        _gc.Initialize(default, default);
        // Missing: Register the device buffer

        _detectionEventManager = new GameCardDeviceDetectionEventManager();
        _isInitialized = true;

        return Result.Success;
    }

    private Result EnsureGameCardNormalMode(out GameCardHandle outNewHandle)
    {
        UnsafeHelpers.SkipParamInit(out outNewHandle);

        if (_state == CardState.Normal)
            CheckGameCardAndDeactivate();

        switch (_state)
        {
            case CardState.Initial:
            {
                // Initial -> Normal
                Result rc = ActivateGameCard();
                if (rc.IsFailure()) return rc.Miss();
                _state = CardState.Normal;

                break;
            }
            case CardState.Normal:
            {
                outNewHandle = _currentHandle;
                return Result.Success;
            }
            case CardState.Secure:
            {
                // Secure -> Initial -> Normal
                DeactivateAndChangeState();

                Result rc = ActivateGameCard();
                if (rc.IsFailure()) return rc.Miss();
                _state = CardState.Normal;

                break;
            }
            case CardState.Write:
            {
                // Write -> Initial -> Normal
                DeactivateAndChangeState();
                _gc.Writer.ChangeMode(AsicMode.Read);

                Result rc = ActivateGameCard();
                if (rc.IsFailure()) return rc.Miss();
                _state = CardState.Normal;

                break;
            }
            default:
                Abort.UnexpectedDefault();
                break;
        }

        outNewHandle = _currentHandle;
        return Result.Success;
    }

    private Result EnsureGameCardSecureMode(out GameCardHandle outNewHandle)
    {
        UnsafeHelpers.SkipParamInit(out outNewHandle);

        if (_state == CardState.Secure)
            CheckGameCardAndDeactivate();

        switch (_state)
        {
            case CardState.Initial:
            {
                // Initial -> Normal -> Secure
                Result rc = ActivateGameCard();
                if (rc.IsFailure()) return rc.Miss();
                _state = CardState.Normal;

                rc = SetGameCardToSecureMode();
                if (rc.IsFailure()) return rc.Miss();
                _state = CardState.Secure;

                break;
            }
            case CardState.Normal:
            {
                // Normal -> Secure
                Result rc = SetGameCardToSecureMode();
                if (rc.IsFailure()) return rc.Miss();
                _state = CardState.Secure;

                break;
            }
            case CardState.Secure:
            {
                outNewHandle = _currentHandle;
                return Result.Success;
            }
            case CardState.Write:
            {
                // Write -> Initial -> Normal -> Secure
                DeactivateAndChangeState();
                _gc.Writer.ChangeMode(AsicMode.Read);

                Result rc = ActivateGameCard();
                if (rc.IsFailure()) return rc.Miss();
                _state = CardState.Normal;

                rc = SetGameCardToSecureMode();
                if (rc.IsFailure()) return rc.Miss();
                _state = CardState.Secure;

                break;
            }
            default:
                Abort.UnexpectedDefault();
                break;
        }

        outNewHandle = _currentHandle;
        return Result.Success;
    }

    private Result EnsureGameCardWriteMode(out GameCardHandle outNewHandle)
    {
        UnsafeHelpers.SkipParamInit(out outNewHandle);

        switch (_state)
        {
            case CardState.Initial:
            {
                // Initial -> Write
                _gc.Writer.ChangeMode(AsicMode.Write);
                Result rc = ActivateGameCardForWriter();
                if (rc.IsFailure()) return rc.Miss();
                _state = CardState.Write;

                break;
            }
            case CardState.Normal:
            case CardState.Secure:
            {
                // Normal/Secure -> Initial -> Write
                DeactivateAndChangeState();

                _gc.Writer.ChangeMode(AsicMode.Write);
                Result rc = ActivateGameCardForWriter();
                if (rc.IsFailure()) return rc.Miss();
                _state = CardState.Write;

                break;
            }
            case CardState.Write:
            {
                outNewHandle = _currentHandle;
                return Result.Success;
            }
            default:
                Abort.UnexpectedDefault();
                break;
        }

        outNewHandle = _currentHandle;
        return Result.Success;
    }

    public Result IsHandleValid(out bool isValid, GameCardHandle handle)
    {
        UnsafeHelpers.SkipParamInit(out isValid);

        Result rc = InitializeGcLibrary();
        if (rc.IsFailure()) return rc.Miss();

        using var readLock = new SharedLock<ReaderWriterLock>();
        isValid = AcquireReadLock(ref readLock.Ref(), handle).IsSuccess();

        return Result.Success;
    }

    public Result OpenDetectionEvent(ref SharedRef<IEventNotifier> outDetectionEvent)
    {
        Result rc = InitializeGcLibrary();
        if (rc.IsFailure()) return rc.Miss();

        rc = _detectionEventManager.CreateDetectionEvent(ref outDetectionEvent);
        if (rc.IsFailure()) return rc.Miss();

        return Result.Success;
    }

    public Result OpenOperator(ref SharedRef<IStorageDeviceOperator> outDeviceOperator)
    {
        throw new NotImplementedException();
    }

    public Result OpenDevice(ref SharedRef<IStorageDevice> outStorageDevice, ulong attribute)
    {
        throw new NotImplementedException();
    }

    public Result OpenStorage(ref SharedRef<IStorage> outStorage, ulong attribute)
    {
        throw new NotImplementedException();
    }

    public Result PutToSleep()
    {
        using var writeLock = new UniqueLock<ReaderWriterLock>(_rwLock);
        _gc.PutToSleep();

        return Result.Success;
    }

    public Result Awaken()
    {
        using var writeLock = new UniqueLock<ReaderWriterLock>(_rwLock);
        _gc.Awaken();

        return Result.Success;
    }

    public Result Shutdown()
    {
        using var writeLock = new UniqueLock<ReaderWriterLock>(_rwLock);
        _gc.PutToSleep();

        return Result.Success;
    }

    public Result Invalidate()
    {
        using var writeLock = new UniqueLock<ReaderWriterLock>(_rwLock);
        DeactivateAndChangeState();

        return Result.Success;
    }

    public Result Operate(int operationId)
    {
        var operation = (GameCardManagerOperationIdValue)operationId;

        Result rc = InitializeGcLibrary();
        if (rc.IsFailure()) return rc.Miss();

        switch (operation)
        {
            case GameCardManagerOperationIdValue.Finalize:
                FinalizeGcLibrary();
                return Result.Success;

            case GameCardManagerOperationIdValue.GetInitializationResult:
                return GetInitializationResult().Ret();

            case GameCardManagerOperationIdValue.ForceErase:
                return ForceEraseGameCard().Ret();

            case GameCardManagerOperationIdValue.SimulateDetectionEventSignaled:
                _detectionEventManager.SignalAll();
                return Result.Success;

            default:
                return ResultFs.InvalidArgument.Log();
        }
    }

    public Result OperateIn(InBuffer buffer, long offset, long size, int operationId)
    {
        var operation = (GameCardManagerOperationIdValue)operationId;

        Result rc = InitializeGcLibrary();
        if (rc.IsFailure()) return rc.Miss();

        switch (operation)
        {
            case GameCardManagerOperationIdValue.SetVerifyEnableFlag:
                if (buffer.Size < sizeof(bool))
                    return ResultFs.InvalidArgument.Log();

                SetVerifyEnableFlag(buffer.As<bool>());
                return Result.Success;

            case GameCardManagerOperationIdValue.EraseAndWriteParamDirectly:
                if (buffer.Size < Unsafe.SizeOf<DevCardParameter>())
                    return ResultFs.InvalidArgument.Log();

                rc = EraseAndWriteParamDirectly(buffer.Buffer);
                if (rc.IsFailure()) return rc.Miss();

                return Result.Success;

            default:
                return ResultFs.InvalidArgument.Log();
        }
    }

    public Result OperateOut(out long bytesWritten, OutBuffer buffer, int operationId)
    {
        var operation = (GameCardManagerOperationIdValue)operationId;
        bytesWritten = 0;

        Result rc = InitializeGcLibrary();
        if (rc.IsFailure()) return rc.Miss();

        switch (operation)
        {
            case GameCardManagerOperationIdValue.GetHandle:
            {
                using var writeLock = new UniqueLock<ReaderWriterLock>(_rwLock);

                if (buffer.Size < sizeof(GameCardHandle))
                    return ResultFs.InvalidArgument.Log();

                rc = GetHandle(out buffer.As<GameCardHandle>());
                if (rc.IsFailure()) return rc.Miss();

                bytesWritten = sizeof(GameCardHandle);
                return Result.Success;
            }
            case GameCardManagerOperationIdValue.GetGameCardErrorInfo:
                if (buffer.Size < Unsafe.SizeOf<GameCardErrorInfo>())
                    return ResultFs.InvalidArgument.Log();

                rc = GetGameCardErrorInfo(out buffer.As<GameCardErrorInfo>());
                if (rc.IsFailure()) return rc.Miss();

                bytesWritten = Unsafe.SizeOf<GameCardErrorInfo>();
                return Result.Success;

            case GameCardManagerOperationIdValue.GetGameCardErrorReportInfo:
                if (buffer.Size < Unsafe.SizeOf<GameCardErrorReportInfo>())
                    return ResultFs.InvalidArgument.Log();

                rc = GetGameCardErrorReportInfo(out buffer.As<GameCardErrorReportInfo>());
                if (rc.IsFailure()) return rc.Miss();

                bytesWritten = Unsafe.SizeOf<GameCardErrorReportInfo>();
                return Result.Success;

            case GameCardManagerOperationIdValue.ReadParamDirectly:
                if (buffer.Size < Unsafe.SizeOf<DevCardParameter>())
                    return ResultFs.InvalidArgument.Log();

                rc = ReadParamDirectly(buffer.Buffer);
                if (rc.IsFailure()) return rc.Miss();

                bytesWritten = Unsafe.SizeOf<DevCardParameter>();
                return Result.Success;

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
        var operation = (GameCardManagerOperationIdValue)operationId;
        bytesWritten = 0;

        Result rc = InitializeGcLibrary();
        if (rc.IsFailure()) return rc.Miss();

        switch (operation)
        {
            case GameCardManagerOperationIdValue.IsGameCardActivationValid:
                if (inBuffer.Size != sizeof(GameCardHandle))
                    return ResultFs.InvalidArgument.Log();

                if (outBuffer.Size < sizeof(bool))
                    return ResultFs.InvalidArgument.Log();

                outBuffer.As<bool>() = IsGameCardActivationValid(inBuffer.As<GameCardHandle>());
                bytesWritten = sizeof(bool);

                return Result.Success;

            case GameCardManagerOperationIdValue.GetGameCardAsicInfo:
                if (inBuffer.Size != Values.GcAsicFirmwareSize)
                    return ResultFs.InvalidArgument.Log();

                if (outBuffer.Size < Unsafe.SizeOf<RmaInformation>())
                    return ResultFs.InvalidArgument.Log();

                rc = GetGameCardAsicInfo(out RmaInformation rmaInfo, inBuffer.Buffer);
                if (rc.IsFailure()) return rc.Miss();

                SpanHelpers.AsReadOnlyByteSpan(in rmaInfo).CopyTo(outBuffer.Buffer);
                bytesWritten = Unsafe.SizeOf<RmaInformation>();

                return Result.Success;

            case GameCardManagerOperationIdValue.GetGameCardDeviceIdForProdCard:
                if (inBuffer.Size < Values.GcPageSize)
                    return ResultFs.InvalidArgument.Log();

                if (outBuffer.Size < Values.GcPageSize)
                    return ResultFs.InvalidArgument.Log();

                rc = GetGameCardDeviceIdForProdCard(outBuffer.Buffer, inBuffer.Buffer);
                if (rc.IsFailure()) return rc.Miss();

                bytesWritten = Values.GcPageSize;

                return Result.Success;

            case GameCardManagerOperationIdValue.WriteToGameCardDirectly:
                return WriteToGameCardDirectly(offset, outBuffer.Buffer.Slice(0, (int)size)).Ret();

            default:
                return ResultFs.InvalidArgument.Log();
        }
    }

    public Result OperateIn2Out(out long bytesWritten, OutBuffer outBuffer, InBuffer inBuffer1, InBuffer inBuffer2,
        long offset, long size, int operationId)
    {
        UnsafeHelpers.SkipParamInit(out bytesWritten);

        return ResultFs.NotImplemented.Log();
    }

    private void FinalizeGcLibrary()
    {
        using var writeLock = new UniqueLock<ReaderWriterLock>(_rwLock);

        if (_isInitialized)
        {
            _gc.UnregisterDetectionEventCallback();
            _isFinalized = true;
            _gc.FinalizeGc();
            // nn::gc::UnregisterDeviceVirtualAddress
        }
    }

    private bool IsGameCardActivationValid(GameCardHandle handle)
    {
        using var writeLock = new UniqueLock<ReaderWriterLock>(_rwLock);

        return handle == _currentHandle && _gc.IsCardActivationValid();
    }

    private Result GetInitializationResult()
    {
        return _gc.GetInitializationResult();
    }

    private Result GetGameCardErrorInfo(out GameCardErrorInfo outErrorInfo)
    {
        outErrorInfo = default;

        Result rc = _gc.GetErrorInfo(out GameCardErrorReportInfo errorInfo);
        if (rc.IsFailure()) return rc.Miss();

        outErrorInfo.GameCardCrcErrorCount = errorInfo.ErrorInfo.GameCardCrcErrorCount;
        outErrorInfo.AsicCrcErrorCount = errorInfo.ErrorInfo.AsicCrcErrorCount;
        outErrorInfo.RefreshCount = errorInfo.ErrorInfo.RefreshCount;
        outErrorInfo.TimeoutRetryErrorCount = errorInfo.ErrorInfo.TimeoutRetryErrorCount;
        outErrorInfo.ReadRetryCount = errorInfo.ErrorInfo.ReadRetryCount;

        return Result.Success;
    }

    private Result GetGameCardErrorReportInfo(out GameCardErrorReportInfo outErrorInfo)
    {
        Result rc = _gc.GetErrorInfo(out outErrorInfo);
        if (rc.IsFailure()) return rc.Miss();

        return Result.Success;
    }

    private void SetVerifyEnableFlag(bool isEnabled)
    {
        _gc.Writer.SetVerifyEnableFlag(isEnabled);
    }

    private Result GetGameCardAsicInfo(out RmaInformation outRmaInfo, ReadOnlySpan<byte> asicFirmwareBuffer)
    {
        UnsafeHelpers.SkipParamInit(out outRmaInfo);

        Assert.SdkRequiresEqual(asicFirmwareBuffer.Length, Values.GcAsicFirmwareSize);

        _gc.Writer.SetUserAsicFirmwareBuffer(asicFirmwareBuffer);
        _gc.Writer.ChangeMode(AsicMode.Write);

        Result rc = _gc.Writer.GetRmaInformation(out RmaInformation rmaInfo);
        if (rc.IsFailure()) return rc.Miss();

        outRmaInfo = rmaInfo;
        return Result.Success;
    }

    private Result GetGameCardDeviceIdForProdCard(Span<byte> outBuffer, ReadOnlySpan<byte> devHeaderBuffer)
    {
        Assert.SdkRequiresGreaterEqual(outBuffer.Length, Values.GcPageSize);
        Assert.SdkRequiresGreaterEqual(devHeaderBuffer.Length, Values.GcPageSize);

        Result rc = InitializeGcLibrary();
        if (rc.IsFailure()) return rc.Miss();

        using var writeLock = new UniqueLock<ReaderWriterLock>(_rwLock);

        int writeSize = Values.GcPageSize;
        var pooledBuffer = new PooledBuffer(writeSize, writeSize);
        Assert.SdkGreaterEqual(pooledBuffer.GetSize(), writeSize);

        // Read the current card header into a temporary buffer
        _gc.Writer.ChangeMode(AsicMode.Read);

        Span<byte> tmpBuffer = stackalloc byte[writeSize];
        tmpBuffer.Clear();

        _gc.GetCardHeader(pooledBuffer.GetBuffer());
        if (rc.IsFailure()) return rc.Miss();

        pooledBuffer.GetBuffer().CopyTo(tmpBuffer);

        // Write the provided card header
        _gc.Writer.ChangeMode(AsicMode.Write);
        rc = HandleGameCardAccessResult(_gc.Writer.ActivateForWriter());
        if (rc.IsFailure()) return rc.Miss();

        devHeaderBuffer.CopyTo(pooledBuffer.GetBuffer());
        rc = _gc.Writer.Write(pooledBuffer.GetBuffer(), 8, 1);
        if (rc.IsFailure()) return rc.Miss();

        // Read the cert area
        _gc.Writer.ChangeMode(AsicMode.Read);
        rc = _gc.Activate();
        if (rc.IsFailure()) return rc.Miss();

        rc = _gc.Read(pooledBuffer.GetBuffer(), 0x38, 1);
        if (rc.IsFailure()) return rc.Miss();

        Span<byte> deviceCert = stackalloc byte[writeSize];
        pooledBuffer.GetBuffer().CopyTo(deviceCert);

        // Restore the original card header
        _gc.Writer.ChangeMode(AsicMode.Write);
        rc = HandleGameCardAccessResult(_gc.Writer.ActivateForWriter());
        if (rc.IsFailure()) return rc.Miss();

        tmpBuffer.CopyTo(pooledBuffer.GetBuffer());
        rc = _gc.Writer.Write(pooledBuffer.GetBuffer(), 8, 1);
        if (rc.IsFailure()) return rc.Miss();

        deviceCert.CopyTo(outBuffer);
        return Result.Success;
    }

    private Result EraseAndWriteParamDirectly(ReadOnlySpan<byte> inBuffer)
    {
        Assert.SdkRequires(inBuffer.Length >= Unsafe.SizeOf<DevCardParameter>());

        Result rc = InitializeGcLibrary();
        if (rc.IsFailure()) return rc.Miss();

        using var writeLock = new UniqueLock<ReaderWriterLock>(_rwLock);

        var devCardParam = SpanHelpers.AsReadOnlyStruct<DevCardParameter>(inBuffer);
        return _gc.Writer.WriteDevCardParam(in devCardParam).Ret();
    }

    private Result ReadParamDirectly(Span<byte> outBuffer)
    {
        Result rc = InitializeGcLibrary();
        if (rc.IsFailure()) return rc.Miss();

        using var writeLock = new UniqueLock<ReaderWriterLock>(_rwLock);

        rc = _gc.Writer.ReadDevCardParam(out DevCardParameter devCardParam);
        if (rc.IsFailure()) return rc.Miss();

        SpanHelpers.AsReadOnlyByteSpan(in devCardParam).CopyTo(outBuffer);
        return Result.Success;
    }

    private Result WriteToGameCardDirectly(long offset, Span<byte> buffer)
    {
        Result rc;

        using (new SharedLock<ReaderWriterLock>(_rwLock))
        {
            if (buffer.Length == 0)
                return Result.Success;

            rc = _gc.Writer.Write(buffer, BytesToPages(offset), BytesToPages(buffer.Length));
        }

        if (rc != Result.Success)
        {
            using var writeLock = new UniqueLock<ReaderWriterLock>(_rwLock);
            rc = HandleGameCardAccessResult(rc);
        }

        if (rc.IsFailure()) return rc.Miss();

        return Result.Success;
    }

    private Result ForceEraseGameCard()
    {
        Result rc = InitializeGcLibrary();
        if (rc.IsFailure()) return rc.Miss();

        using var writeLock = new UniqueLock<ReaderWriterLock>(_rwLock);

        _gc.Writer.ChangeMode(AsicMode.Write);
        rc = _gc.Writer.ForceErase();
        if (rc.IsFailure()) return rc.Miss();

        return Result.Success;
    }

    public Result AcquireReadLock(ref SharedLock<ReaderWriterLock> outLock, GameCardHandle handle)
    {
        using var readLock = new SharedLock<ReaderWriterLock>(_rwLock);

        if (_state != CardState.Initial && !_gc.IsCardActivationValid())
        {
            readLock.Unlock();
            Invalidate().IgnoreResult();

            return ResultFs.GameCardFsCheckHandleInAcquireReadLock.Log();
        }

        if (_currentHandle != handle)
            return ResultFs.GameCardFsCheckHandleInAcquireReadLock.Log();

        outLock.Set(ref readLock.Ref());
        return Result.Success;
    }

    public Result AcquireSecureLock(ref SharedLock<ReaderWriterLock> outLock, ref GameCardHandle handle,
        ReadOnlySpan<byte> cardDeviceId, ReadOnlySpan<byte> cardImageHash)
    {
        using (var readLock = new SharedLock<ReaderWriterLock>(_rwLock))
        {
            if (!IsSecureMode())
            {
                return ResultFs.GameCardFsCheckModeInAcquireSecureLock.Log();
            }

            if (_state != CardState.Initial && !_gc.IsCardActivationValid())
            {
                readLock.Unlock();
                Invalidate().IgnoreResult();
            }
            else if (_currentHandle == handle)
            {
                outLock.Set(ref readLock.Ref());
                return Result.Success;
            }
        }

        GameCardHandle newHandle;

        using (new UniqueLock<ReaderWriterLock>(_rwLock))
        {
            if (!IsSecureMode())
            {
                return ResultFs.GameCardFsCheckModeInAcquireSecureLock.Log();
            }

            Span<byte> currentCardDeviceId = stackalloc byte[Values.GcCardDeviceIdSize];
            Span<byte> currentCardImageHash = stackalloc byte[Values.GcCardImageHashSize];

            Result rc = HandleGameCardAccessResult(_gc.GetCardDeviceId(currentCardDeviceId));
            if (rc.IsFailure()) return rc.Miss();

            rc = HandleGameCardAccessResult(_gc.GetCardImageHash(currentCardImageHash));
            if (rc.IsFailure()) return rc.Miss();

            if (!Crypto.CryptoUtil.IsSameBytes(currentCardDeviceId, cardDeviceId, Values.GcCardDeviceIdSize) ||
                !Crypto.CryptoUtil.IsSameBytes(currentCardImageHash, cardImageHash, Values.GcCardImageHashSize))
                return ResultFs.GameCardFsCheckModeInAcquireSecureLock.Log();

            rc = GetHandle(out newHandle);
            if (rc.IsFailure()) return rc.Miss();
        }

        using (var readLock = new SharedLock<ReaderWriterLock>())
        {
            Result rc = AcquireReadLock(ref readLock.Ref(), newHandle);
            if (rc.IsFailure()) return rc.Miss();

            handle = newHandle;
            outLock.Set(ref readLock.Ref());

            return Result.Success;
        }
    }

    public Result AcquireWriteLock(ref UniqueLock<ReaderWriterLock> outLock)
    {
        Result rc = InitializeGcLibrary();
        if (rc.IsFailure()) return rc.Miss();

        using var writeLock = new UniqueLock<ReaderWriterLock>(_rwLock);
        outLock.Set(ref writeLock.Ref());

        return Result.Success;
    }

    public Result HandleGameCardAccessResult(Result result)
    {
        if (result.IsFailure())
        {
            DeactivateAndChangeState();
        }

        return result;
    }

    public Result GetHandle(out GameCardHandle outHandle)
    {
        UnsafeHelpers.SkipParamInit(out outHandle);

        if (_state == CardState.Normal || _state == CardState.Secure)
        {
            CheckGameCardAndDeactivate();
        }

        switch (_state)
        {
            case CardState.Initial:
            {
                Result rc = ActivateGameCard();
                if (rc.IsFailure()) return rc.Miss();

                break;
            }
            case CardState.Normal:
            case CardState.Secure:
                break;
            case CardState.Write:
            {
                DeactivateAndChangeState();
                _gc.Writer.ChangeMode(AsicMode.Read);

                Result rc = ActivateGameCard();
                if (rc.IsFailure()) return rc.Miss();

                _state = CardState.Normal;
                break;
            }
            default:
                Abort.UnexpectedDefault();
                break;
        }

        outHandle = _currentHandle;
        return Result.Success;
    }

    public bool IsSecureMode()
    {
        return _state == CardState.Secure;
    }
}