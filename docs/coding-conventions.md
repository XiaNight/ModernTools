## Formatting

- Indentation: tabs, width 4 (not spaces)
- Braces: Allman — open brace on its own line, required even for single-line `if`/`for` bodies
- Guard clauses: a simple guard that exits immediately may stay on one line without braces — e.g. `if (condition) break;` (also `return`/`continue`)
- Namespaces: file-scoped (`namespace Foo;`), matching the folder path
- Types: explicit, no `var` (target-typed `new` is fine, e.g. `StringBuilder sb = new();`)
- Accessibility modifiers: required on all non-interface members
- Primary constructors: prefer where they fit
- Expression-bodied: properties, accessors, indexers, lambdas, local functions — block bodies for methods, constructors, operators
- Pattern matching: prefer over `is`/`as`-with-cast; switch expressions over statements; `null` checks over reference-equality/type checks
- Parentheses: add for clarity in arithmetic, relational, and other binary operators
- Collection expressions (`[...]`): prefer when the target type matches (e.g. `Path = ["Mouse"]`)
- `readonly`: only for fields that are conceptually immutable

## Naming

- PascalCase: types, methods, properties, events, constants
- camelCase: private fields (e.g. `reportRateSmoothed`, `stopwatch`)
- `I`-prefix: interfaces
