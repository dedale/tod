# Contributing to Tod

Thank you for your interest in contributing to Tod!

## Development Setup

1. **Prerequisites**
   - .NET 10 SDK or later
   - Git
   - Visual Studio 2026 or later (recommended)

2. **Clone the repository**
   ```
   git clone https://github.com/dedale/tod.git
   cd tod
   ```

3. **Restore dependencies**
   ```
   dotnet restore
   ```

4. **Build the project**
   ```
   dotnet build
   ```

5. **Run tests**
   ```
   dotnet test
   ```

## Coding Standards

Please follow the coding standards defined in `.github/copilot-instructions.md`.

## Pull Request Process

1. **Create a feature branch**
   ```
   git checkout -b feature/your-feature-name
   ```

2. **Make your changes**
   - Write clean, maintainable code
   - Add tests for new functionality
   - Update documentation as needed

3. **Run tests locally**
   ```
   dotnet test
   ```

4. **Commit your changes**
   ```
   git commit -m "Add feature: your feature description"
   ```

5. **Push to your fork**
   ```
   git push origin feature/your-feature-name
   ```

6. **Open a Pull Request**
- Use the PR template
- Describe your changes clearly
- Link any related issues

## Testing

- All new code should include unit tests
- Maintain or improve code coverage (you may use tools like Coverlet or Fine Code Coverage extension).
- Please follow the testing standards defined in `.github/copilot-instructions.md`.

## Questions?

Feel free to open an issue for any questions or concerns.
