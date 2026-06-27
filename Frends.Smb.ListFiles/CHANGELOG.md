# Changelog

## [2.2.0] - 2026-06-26

### Changed

- Username parsing: now accepts a username without a domain instead of throwing an error.
- Server connection: removed manual DNS resolve; connects using the raw server address string.

## [2.1.0] - 2026-04-23

### Fixed

- Input parameters treated as normal string instead of PathString type.

## [2.0.0] - 2026-04-08

### Added

- New connection parameters that defined what servers Operating System.
- [Breaking Change] Introduce PathString type that will represent paths with OS specific separators.

## [1.0.0] - 2025-11-07

### Added

- Initial implementation
