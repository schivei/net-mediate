// @ts-check
// `@type` JSDoc annotations allow editor autocompletion and type checking
// (when paired with `@ts-check`).
// There are various equivalent ways to declare your Docusaurus config.
// See: https://docusaurus.io/docs/api/docusaurus-config

import {themes as prismThemes} from 'prism-react-renderer';

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'NetMediate',
  tagline: 'A lightweight and efficient .NET implementation of the Mediator pattern',
  favicon: 'img/favicon.ico',

  // Set the production url of your site here
  url: 'https://schivei.github.io',
  // Set the /<baseUrl>/ pathname under which your site is served
  // For GitHub pages deployment, it is often '/<projectName>/'
  baseUrl: '/net-mediate/',

  // GitHub pages deployment config.
  // If you aren't using GitHub pages, you don't need these.
  organizationName: 'schivei', // Usually your GitHub org/user name.
  projectName: 'net-mediate', // Usually your repo name.

  onBrokenLinks: 'warn',
  onBrokenMarkdownLinks: 'warn',

  // Even if you don't use internationalization, you can use this field to set
  // useful metadata like html lang. For example, if your site is Chinese, you
  // may want to replace "en" with "zh-Hans".
  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          sidebarPath: './sidebars.js',
          // Please change this to your repo.
          // Remove this to remove the "edit this page" links.
          editUrl:
            'https://github.com/schivei/net-mediate/tree/main/website/',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      }),
    ],
  ],

  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      // Replace with your project's social card
      image: 'img/netmediate-social-card.jpg',
      navbar: {
        title: 'NetMediate',
        logo: {
          alt: 'NetMediate Logo',
          src: 'img/logo.svg',
        },
        items: [
          {
            type: 'docSidebar',
            sidebarId: 'tutorialSidebar',
            position: 'left',
            label: 'Documentation',
          },
          {
            href: 'https://github.com/schivei/net-mediate',
            label: 'GitHub',
            position: 'right',
          },
          {
            href: 'https://www.nuget.org/packages/NetMediate/',
            label: 'NuGet',
            position: 'right',
          },
        ],
      },
      footer: {
        style: 'dark',
        links: [
          {
            title: 'Docs',
            items: [
              {
                label: 'Getting Started',
                to: '/docs/intro',
              },
              {
                label: 'Installation',
                to: '/docs/getting-started/installation',
              },
              {
                label: 'API Reference',
                to: '/docs/api/core-interfaces',
              },
            ],
          },
          {
            title: 'Community',
            items: [
              {
                label: 'GitHub',
                href: 'https://github.com/schivei/net-mediate',
              },
              {
                label: 'Issues',
                href: 'https://github.com/schivei/net-mediate/issues',
              },
              {
                label: 'Discussions',
                href: 'https://github.com/schivei/net-mediate/discussions',
              },
            ],
          },
          {
            title: 'More',
            items: [
              {
                label: 'NuGet',
                href: 'https://www.nuget.org/packages/NetMediate/',
              },
              {
                label: 'Contributing',
                to: '/docs/community/contributing',
              },
              {
                label: 'License',
                href: 'https://github.com/schivei/net-mediate/blob/main/LICENSE',
              },
            ],
          },
        ],
        copyright: `Copyright © ${new Date().getFullYear()} NetMediate. Built with Docusaurus.`,
      },
      prism: {
        theme: prismThemes.github,
        darkTheme: prismThemes.dracula,
        additionalLanguages: ['csharp', 'bash', 'powershell', 'json'],
      },
      colorMode: {
        defaultMode: 'light',
        disableSwitch: false,
        respectPrefersColorScheme: true,
      },
      announcementBar: {
        id: 'announcement',
        content:
          '⭐️ If you like NetMediate, give it a star on <a target="_blank" rel="noopener noreferrer" href="https://github.com/schivei/net-mediate">GitHub</a>! ⭐️',
        backgroundColor: '#20232a',
        textColor: '#fff',
        isCloseable: true,
      },
    }),
};

export default config;
