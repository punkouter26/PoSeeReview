# Documentation Diagrams

This folder contains architecture diagrams in Mermaid format (`.mmd` files) and their generated SVG outputs.

## Viewing Diagrams

### In VS Code
Install the [Mermaid Preview](https://marketplace.visualstudio.com/items?itemName=bierner.markdown-mermaid) extension to view `.mmd` files directly.

### In GitHub/Markdown
SVG files are embedded in the main README.md and can be viewed directly in any markdown viewer.

## Generating SVG Files

The diagrams are stored as `.mmd` (Mermaid) source files and converted to `.svg` using the Mermaid CLI.

### Prerequisites

```powershell
# Install Node.js (if not already installed)
# Download from: https://nodejs.org/

# Verify installation
node --version
npm --version
```

### Build Diagrams

```powershell
# From the docs folder
cd docs

# Install dependencies (first time only)
npm install

# Generate all SVG files
npm run build-diagrams

# Watch for changes and auto-generate
npm run build-diagrams:watch

# Clean generated SVG files
npm run clean-diagrams
```

## Available Diagrams

### C4 Context Diagram (`c4-context.mmd`)
Shows the system context - SeeReview and its relationship with users and external systems.

### C4 Container Diagram (`c4-container.mmd`)
Shows the high-level technical building blocks: Blazor WASM client, ASP.NET Core API, Azure services, and external APIs.

### Sequence Diagram - Comic Generation (`sequence-comic-generation.mmd`)
Illustrates the complete flow of generating a comic strip from user interaction to final display.

### Component Architecture (`component-architecture.mmd`)
Shows the internal structure: frontend components, API layers, business logic, and infrastructure.

## Editing Diagrams

1. Edit the `.mmd` source file
2. Run `npm run build-diagrams` to regenerate SVGs
3. Commit both `.mmd` and `.svg` files to Git

**Why commit SVGs?** SVG files ensure diagrams are visible in GitHub, README files, and documentation sites without requiring build steps.

## Mermaid Syntax Reference

- [Official Mermaid Documentation](https://mermaid.js.org/)
- [Mermaid Live Editor](https://mermaid.live/) - Test diagrams online
- [C4 Model](https://c4model.com/) - Context, Container, Component, Code diagrams

## Troubleshooting

### Error: `mmdc: command not found`

Run `npm install` in the `docs` folder first.

### SVG files not generating

Ensure all `.mmd` files are syntactically correct. Use [Mermaid Live Editor](https://mermaid.live/) to validate.

### Diagram theme/colors

Diagrams use the `neutral` theme with transparent background. To change, edit the `build-diagrams` script in `package.json`:

```json
"build-diagrams": "mmdc -i diagrams/*.mmd -o diagrams/ -t dark -b #1e1e1e"
```
