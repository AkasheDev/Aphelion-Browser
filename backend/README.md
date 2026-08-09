# Aphelion Backend

Optional synchronization backend built on .NET 10.

## Dependency rule

- `Aphelion.Api.Domain` has no project dependencies.
- `Aphelion.Api.Application` depends only on Domain.
- `Aphelion.Api.Infrastructure` implements Application ports and depends on Application and Domain.
- `Aphelion.Api` is the composition and transport boundary; it depends on Application and Infrastructure.

Third-party persistence, identity, observability, and test packages will be selected only after explicit approval and architecture decisions.
