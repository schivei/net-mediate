// @ts-check

/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  tutorialSidebar: [
    {
      type: 'doc',
      id: 'intro',
      label: 'Introduction',
    },
    ...([
      ['Getting Started', [
        'getting-started/installation',
        'getting-started/quick-start',
        'getting-started/message-types',
        'getting-started/handlers',
        'getting-started/installation-config',
      ]],
      ['Usage Guides', [
        'guides/commands',
        'guides/requests',
        'guides/notifications',
        'guides/streams',
        'guides/pipeline-behaviors',
        'guides/validation',
        'guides/samples',
      ]],
      ['Advanced Topics', [
        'advanced/source-generation',
        'advanced/aot-support',
        'advanced/resilience',
        'advanced/diagnostics',
        'advanced/quartz',
        'advanced/datadog',
      ]],
      ['Testing', [
        'testing/moq-recipes',
        'testing/unit-testing',
      ]],
      ['Performance', [
        'performance/benchmarks',
        'performance/best-practices',
      ]],
      ['API Reference', [
        'api/core-interfaces',
        'api/handlers',
        'api/behaviors',
        'api/extensions',
      ]],
      ['Community', [
        'community/contributing',
        'community/code-of-conduct',
        'community/security',
        'community/roadmap',
      ]],
    ].map(([label, items]) => ({ type: 'category', label, items }))),
  ],
};

export default sidebars;
