# NetMediate Documentation Website

This directory contains the source code for the NetMediate documentation website, built with [Docusaurus](https://docusaurus.io/).

## Local Development

### Prerequisites

- Node.js 18.0 or higher
- npm (comes with Node.js)

### Installation

```bash
cd website
npm install
```

### Start Development Server

```bash
npm start
```

This command starts a local development server and opens up a browser window. Most changes are reflected live without having to restart the server.

### Build

```bash
npm run build
```

This command generates static content into the `build` directory that can be served using any static content hosting service.

## Project Structure

```
website/
├── docs/              # Documentation pages (Markdown)
├── src/
│   ├── css/          # Custom CSS
│   └── pages/        # Custom React pages
├── static/           # Static assets (images, etc.)
├── docusaurus.config.js  # Docusaurus configuration
├── sidebars.js       # Sidebar navigation structure
└── package.json      # Dependencies
```

## Deployment

The documentation is automatically deployed to GitHub Pages when changes are pushed to the `main` branch via GitHub Actions. See `.github/workflows/deploy-docs.yml` for the deployment workflow.

## Contributing

When adding or updating documentation:

1. Create or edit Markdown files in the `docs/` directory
2. Update `sidebars.js` if adding new pages
3. Test locally with `npm start`
4. Commit and push changes

## Resources

- [Docusaurus Documentation](https://docusaurus.io/docs)
- [Markdown Features](https://docusaurus.io/docs/markdown-features)
- [MDX](https://mdxjs.com/)
