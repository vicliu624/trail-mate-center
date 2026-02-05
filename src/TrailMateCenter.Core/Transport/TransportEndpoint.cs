namespace TrailMateCenter.Transport;

public abstract record TransportEndpoint;

public sealed record SerialEndpoint(string PortName, int BaudRate = 115200) : TransportEndpoint;

public sealed record ReplayEndpoint(string FilePath, double Speed = 1.0) : TransportEndpoint;
