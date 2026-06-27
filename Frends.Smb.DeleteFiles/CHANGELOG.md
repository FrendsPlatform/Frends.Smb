# Changelog

## [2.3.0] - 2026-06-26

### Changed

- Username parsing: now accepts a username without a domain instead of throwing an error.

## [2.2.0] - 2026-05-20

### Added

- Added ContinueOnFailure as a new option — allows the operation to proceed when individual file copies fail, collecting errors in a failures list instead of throwing immediately

## [2.1.0] - 2026-04-23

### Fixed

- Input parameters treated as normal string instead of PathString type.

## [2.0.0] - 2026-04-08

### Added

- New connection parameters that defined what servers Operating System.
- [Breaking Change] Introduce PathString type that will represent paths with OS specific separators.

## [1.2.0] - 2025-11-14

### Changed

- Update pattern matching

## [1.1.0] - 2025-11-11

### Changed

- Update SMBLibrary version to 1.5.4.1.

## [1.0.0] - 2025-11-04

### Added

- Initial implementation
