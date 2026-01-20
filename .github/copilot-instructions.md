# GitHub Copilot Instructions

## Project Overview
Tod is a Jenkins automation tool for managing CI/CD builds and tests.

## Coding Standards
- Use C# 14.0 features targeting .NET 10
- Follow existing patterns: Mock with MockBehavior.Strict in tests
- Always call VerifyAll() in TearDown methods
- Use primary constructors where appropriate
- Prefer `ConfigureAwait(false)` for async calls
- Use `sealed` classes by default
- Use file-scoped namespaces
- Use `internal` visibility for all types (no public types except when needed)
- Use `record` types for immutable data structures
- Always mark classes as `sealed` unless inheritance is required
- Prefer expression-bodied properties and methods when concise
- Use file-scoped namespaces (no braces)
- Make sure files do not have trailing whitespace at the end of lines

## Code Organization
- Namespace should match folder structure
- Group related models in same file if they're tightly coupled
- Interfaces should start with 'I' prefix
- Use file-scoped namespaces (no braces)
- Keep one primary type per file, unless types are tightly coupled

## Fields and Properties
- Private fields: use `_camelCase` prefix
- Use `readonly` for fields that don't change after construction
- Prefer properties over public fields
- Use init-only setters or constructor parameters for immutability

## Async Patterns
- Always use `ConfigureAwait(false)` for library code
- Use `async Task` for methods that don't return values
- Use `async Task<T>` for methods that return values
- Avoid `async void` except for event handlers

## Error Handling and Validation
- Use `Debug.Assert` for development-time checks (not for user validation)
- Throw `ArgumentException` or derived types for invalid arguments
- Use `[NotNullWhen(true)]` attribute with TryXxx pattern methods
- Custom exceptions should be sealed and include constructor with message and inner exception

## Logging with Serilog
- Use Serilog static `Log` class for logging
- Use structured logging with named placeholders: `Log.Information("Message {Placeholder}", value)`
- Log levels: Debug for detailed flow, Information for key events, Warning for issues, Error for failures
- Never concatenate strings in log messages, use placeholders
- Manage plurals with conditional logic, not in log message templates

## JSON Serialization
- Use `System.Text.Json` with `[JsonConstructor]` for deserialization
- Use `[JsonPropertyName]` to customize property names
- Use `[JsonIgnore]` to exclude properties from serialization
- Create custom converters inheriting from `JsonConverter<T>` when needed
- Make constructors private when using `[JsonConstructor]` and provide static factory methods

## Testing Patterns
- Use NUnit with [TestFixture] and [Test] attributes
- Follow Arrange-Act-Assert structure
- Do not add Arrange/Act/Assert comments 
- Use Mock<T> with Strict behavior and verify all setups
- Use RandomData helper methods for test data generation
- Test method names should be: MethodName_Behavior_Condition
- Use Assert.That for assertions
- Always verify all mocks in TearDown with VerifyAll()
- Use [SetUp] for test initialization, [TearDown] for cleanup
- Test one behavior per test method

## Naming Conventions
- Test classes: {ClassName}Tests
- Test methods: descriptive with underscores (e.g., Validate_ReturnsFalse_WhenThresholdExceeded)
- Private fields: _camelCase
- Use readonly where possible
- Interface implementations: descriptive names matching their purpose
- Avoid Hungarian notation except for private fields (_prefix) and static private fields (s_prefix)

## Disposal Pattern
- Implement IDisposable for classes that manage unmanaged resources or hold locks
- Use `using` statements or declarations for IDisposable objects
- In Dispose, clean up resources and suppress finalization if needed

## Collections and LINQ
- Prefer collection expressions `[...]` over `new[]` or `Array.Empty<T>()`
- Use LINQ for data transformations and queries
- Use `ToDictionary`, `GroupBy`, `Select` for functional operations
- Avoid unnecessary `.ToList()` or `.ToArray()` unless required

## Documentation
- XML comments for public APIs only
- Inline comments only for complex logic that isn't self-explanatory
- Prefer self-documenting code with clear names over comments
