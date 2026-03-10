---
applyTo: "infra/**"
---

# Bicep Development Instructions

## Separation of Concerns

- **Infrastructure deployment** (`main.bicep`) provisions Azure resources only — it should not configure application settings.
- **Application deployment** (`app.bicep`) configures an existing resource (e.g., Web App settings, connection strings) — it should not create infrastructure.
- Each Azure service gets its own **module** under `modules/`. Modules are self-contained and reusable.
- Use `.bicepparam` files for environment-specific values. Never hardcode environment details in `.bicep` files.
- Outputs from the infrastructure deployment feed into the application deployment as parameters.

## General

- Avoid setting the `name` field for `module` statements — it is no longer required.
- If you need to input or output a set of logically-grouped values, use a single `param` or `output` with a User-defined type instead of separate statements for each value.
- Default to Bicep parameters files (`*.bicepparam`) instead of ARM parameters files (`*.json`).
- Use consistent naming conventions: `{prefix}-{service}` (e.g., `awb-dev-cosmos`).

## Resources

- Do not reference parent resources via `/` in the child `name` property. Use the `parent` property with a symbolic reference instead.
- If generating a child resource type, add an `existing` resource for the parent if it is not already present in the file.
- Avoid `resourceId()` and `reference()` functions. Use symbolic names (e.g., `foo.id`, `foo.properties.endpoint`) and create `existing` resources when needed.
- If you see diagnostic codes `BCP036`, `BCP037`, or `BCP081`, double-check the resource type schema — these often indicate hallucinated types or properties.

## Types

- Avoid open types (`array`, `object`) in `param` and `output` statements. Define User-defined types for precise typing.
- Use typed variables when exporting with `@export()` (e.g., `var foo string = 'blah'`).
- Comment User-defined type properties with `@description()` where the context is unclear.
- When passing data to or from a resource body, prefer Resource-derived types (`resourceInput<'type@version'>` and `resourceOutput<'type@version'>`) over hand-written types.

## Security

- Always use the `@secure()` decorator on `param` or `output` statements that contain sensitive data (keys, connection strings, passwords).
- Use `#disable-next-line outputs-should-not-contain-secrets` only when intentionally passing secrets between infra and app deployments — always add a justification comment.
- Prefer managed identity and RBAC role assignments over connection-string-based auth when the consuming code supports it.

## Syntax

- Solve null-property warnings with the safe-dereference (`.?`) and coalesce (`??`) operators (e.g., `a.?b ?? c`) instead of `a!.b` or verbose ternary expressions.

## Formatting

- Only add comments that provide additional context. Do not add decorative demarcation lines (e.g., `// ====`).
- Use `@description()` on all `param` and `output` statements.
- Use `@allowed()`, `@minLength()`, `@maxLength()`, `@minValue()`, `@maxValue()` decorators to constrain parameters where applicable.
