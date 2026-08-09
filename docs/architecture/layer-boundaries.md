# Layer Boundaries

## Mobile

Domain is pure Dart. Application coordinates Domain through use cases and ports. Infrastructure and Platform implement ports. Presentation invokes Application behavior and contains no business rules.

## Backend

Domain has no project references. Application references Domain. Infrastructure references Application and Domain. API is the composition root and references Application and Infrastructure.

## Cross-client

Desktop and mobile share versioned contracts, design sources, and behavioral specifications, never implementation source code.
