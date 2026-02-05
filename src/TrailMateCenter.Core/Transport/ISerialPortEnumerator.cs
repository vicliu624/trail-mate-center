namespace TrailMateCenter.Transport;

public interface ISerialPortEnumerator
{
    Task<IReadOnlyList<SerialPortInfo>> GetPortsAsync(CancellationToken cancellationToken);
}
