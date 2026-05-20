# Changelog

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
