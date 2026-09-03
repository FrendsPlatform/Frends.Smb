# Changelog

## [2.6.0] - 2026-09-03

### Fixed

- Fixed SMB path handling to use the configured OperatingSystem.

## [2.5.0] - 2026-07-27

### Added

- Added Kerberos authentication support for SMB connections
  - New `Connection.AuthenticationMode` property with `Ntlm` (default) and `Kerberos` options
  - New `Connection.KerberosServerName` property for specifying the Kerberos SPN hostname when it differs from the TCP connection address
  - New `Connection.KdcAddress` property for explicit KDC address when DNS SRV discovery is unavailable
  - `KerberosNetAuthenticationClient` internally handles TGT acquisition, service ticket retrieval, and GSS-API token generation using the Kerberos.NET library

## [2.4.0] - 2026-07-20

### Fixed

- Optimized SMB file handling: fixed lingering locks and adjusted access permissions to prevent sharing violations.

## [2.3.0] - 2026-06-26

### Changed

- Username parsing: now accepts a username without a domain instead of throwing an error.
- Server connection: removed manual DNS resolve; connects using the raw server address string.

## [2.2.0] - 2026-05-20

### Added

- Added ContinueOnFailure as a new option — allows the operation to proceed when individual file copies fail, collecting errors in a failures list instead of throwing immediately

### Fixed

- Fixed rollback mechanism — replaced unreliable rename with full byte-by-byte copy.

## [2.1.0] - 2026-04-23

### Fixed

- Input parameters treated as normal string instead of PathString type.

## [2.0.0] - 2026-04-08

### Added

- New connection parameters that defined what servers Operating System.
- [Breaking Change] Introduce PathString type that will represent paths with OS specific separators.

## [1.0.0] - 2025-11-20

### Added

- Initial implementation
