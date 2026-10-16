namespace OPNX.Lib.Network.Protocol.FileTransfer
{
    public enum FileTransferState : byte
    {
        None,
        Preparing,
        Ready,
        Queued,
        Transferring,
        Completed,
        PartiallyCompleted,
        CancelRequested,
        Canceled,
        Failed
    }

    public enum FileTransferFileState : byte
    {
        None,
        Ready,
        Transferring,
        Completed,
        NotFound,
        InvalidPath,
        AccessDenied,
        FileInUse,
        Changed,
        OpenFailed,
        ReadFailed,
        WriteFailed,
        HashMismatch,
        Canceled,
        Failed
    }

    public enum FileTransferFlowMode : byte
    {
        Automatic,
        WaitForClientContinue
    }

    public sealed record FileTransferRequestItem(Guid FileID, string FilePath);

    public sealed record FileTransferSource(Guid FileID, string FilePath);

    public sealed record FileTransferPreparedFile(Guid FileID, string FileName, long Length, long LastWriteTimeUnixMS, FileTransferFileState State, string Message);

    public sealed record FileTransferPrepareResult(Guid TransferID, IReadOnlyList<FileTransferPreparedFile> Files, long TotalLength)
    {
        public bool CanStart => Files.Any(x => x.State == FileTransferFileState.Ready);
    }

    public sealed record FileTransferDownloadedFile(Guid FileID, string SourceFileName, string DestinationPath, long Length, FileTransferFileState State, string Message);

    public sealed record FileTransferResult(Guid TransferID, FileTransferState State, IReadOnlyList<FileTransferDownloadedFile> Files, long TransferredLength, string Message);

    public readonly record struct FileTransferProgress(Guid TransferID, Guid FileID, string FileName, int FileIndex, int FileCount, long FileReceivedLength, long FileLength, long TotalReceivedLength, long TotalLength)
    {
        public double FilePercentage => FileLength <= 0 ? 0 : FileReceivedLength * 100d / FileLength;
        public double TotalPercentage => TotalLength <= 0 ? 0 : TotalReceivedLength * 100d / TotalLength;
    }
}
