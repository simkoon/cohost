namespace CoHost.Contracts;

/// <summary>How the reporting component was installed. Wire: msi | zip | dev.</summary>
public enum InstallKind { Msi, Zip, Dev }

/// <summary>Update package format. Wire: msi | zip.</summary>
public enum PackageKind { Msi, Zip }

/// <summary>Updatable component. Wire: agent | client.</summary>
public enum ComponentKind { Agent, Client }

/// <summary>Why a local (console) session ended. Wire: manual | expired | admin.</summary>
public enum LocalEndReason { Manual, Expired, Admin }

/// <summary>Why a remote session ended. Wire: viewer | local_kick | network.</summary>
public enum RemoteEndReason { Viewer, LocalKick, Network }

/// <summary>Detected bypass route. Wire: rdp | radmin.</summary>
public enum BypassKind { Rdp, Radmin }

/// <summary>Authentication level of a local session. Wire: none | pin | tag.</summary>
public enum AuthLevel { None, Pin, Tag }

/// <summary>Device approval state. Wire: pending | approved | disabled.</summary>
public enum DeviceStatus { Pending, Approved, Disabled }

/// <summary>User role. Wire: admin | user.</summary>
public enum UserRole { Admin, User }
