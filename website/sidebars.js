// @ts-check

/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  tutorialSidebar: [
    {
      type: 'doc',
      id: 'intro',
      label: 'Introduction',
    },
    {
      type: 'category',
      label: 'Getting Started',
      items: [
        'getting-started/installation',
        'getting-started/quick-start',
        'getting-started/message-types',
        'getting-started/handlers',
      ],
    },
    {
      type: 'category',
      label: 'Usage Guides',
      items: [
        'guides/commands',
        'guides/requests',
        'guides/notifications',
        'guides/streams',
        'guides/pipeline-behaviors',
        'guides/validation',
      ],
    },
    {
      type: 'category',
      label: 'Advanced Topics',
      items: [
        'advanced/source-generation',
        'advanced/aot-support',
        'advanced/resilience',
        'advanced/diagnostics',
        'advanced/quartz',
        'advanced/datadog',
      ],
    },
    {
      type: 'category',
      label: 'Testing',
      items: [
        'testing/moq-recipes',
        'testing/unit-testing',
      ],
    },
    {
      type: 'category',
      label: 'Performance',
      items: [
        'performance/benchmarks',
        'performance/best-practices',
      ],
    },
    {
      type: 'category',
      label: 'API Reference',
      items: [
        'api/core-interfaces',
        'api/handlers',
        'api/behaviors',
        'api/extensions',
      ],
    },
    {
      type: 'category',
      label: 'Community',
      items: [
        'community/contributing',
        'community/code-of-conduct',
        'community/security',
        'community/roadmap',
      ],
    },
  ],
};

export default sidebars;
