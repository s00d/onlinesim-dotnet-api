import { readdirSync } from 'node:fs'

const repo = 'onlinesim-dotnet-api'
const docsBase = process.env.NUXT_APP_BASE_URL || (process.env.NODE_ENV === 'production' ? `/${repo}/` : '/')

/** Every content page, so prerender does not depend on link crawling alone. */
const pageRoutes = readdirSync(new URL('./content', import.meta.url))
  .filter((file) => file.endsWith('.md'))
  .map((file) => (file === 'index.md' ? '/' : `/${file.slice(0, -'.md'.length)}`))

export default defineNuxtConfig({
  modules: ['@nuxt/ui', '@nuxt/content', 'nuxt-llms'],

  css: ['~/assets/css/main.css'],

  app: {
    baseURL: docsBase,
    head: {
      htmlAttrs: { lang: 'en' },
      link: [{ rel: 'icon', type: 'image/svg+xml', href: `${docsBase}favicon.svg` }],
    },
  },

  icon: {
    // Bundle every rendered icon: the static host never falls back to the Iconify API.
    clientBundle: {
      scan: true,
      icons: ['simple-icons:nuget', 'simple-icons:dotnet', 'lucide:terminal', 'lucide:file-code'],
      sizeLimitKb: 256,
    },
  },

  colorMode: {
    preference: 'system',
  },

  content: {
    build: {
      markdown: {
        toc: { searchDepth: 2 },
        highlight: { langs: ['csharp', 'bash', 'powershell', 'json', 'xml'] },
      },
    },
    experimental: {
      sqliteConnector: 'native',
    },
  },

  nitro: {
    prerender: {
      // `numbers.html`, not `numbers/index.html`: GitHub Pages serves `/numbers` without a redirect.
      autoSubfolderIndex: false,
      crawlLinks: true,
      failOnError: true,
      routes: pageRoutes,
    },
  },

  llms: {
    domain: `https://s00d.github.io/${repo}`,
    title: 'OnlineSim.Api',
    description: 'Typed async C# / .NET client for the OnlineSim API, with a built-in offline mock.',
    sections: [
      {
        title: 'Documentation',
        contentCollection: 'docs',
        contentFilters: [{ field: 'extension', operator: '=', value: 'md' }],
      },
    ],
    full: {
      title: 'OnlineSim.Api — full docs',
      description: 'Complete documentation of the OnlineSim.Api .NET SDK.',
    },
  },

  compatibilityDate: '2025-11-03',
})
