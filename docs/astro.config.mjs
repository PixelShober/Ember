import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// Published on GitHub Pages: https://pixelshober.github.io/Ember/
export default defineConfig({
  site: 'https://pixelshober.github.io',
  base: '/Ember',
  integrations: [
    starlight({
      title: 'Ember',
      description: 'Richtet Entwicklerrechner fürs Firmennetz ein: Proxy, Softwarecenter, Energie, KI-Werkzeuge.',
      logo: { src: './src/assets/ember.svg' },
      favicon: '/favicon.svg',
      // German is the root language; English can be added later as /en/ without moving pages.
      defaultLocale: 'root',
      locales: {
        root: { label: 'Deutsch', lang: 'de' },
      },
      social: [{ icon: 'github', label: 'GitHub', href: 'https://github.com/PixelShober/Ember' }],
      editLink: { baseUrl: 'https://github.com/PixelShober/Ember/edit/main/docs/' },
      customCss: ['./src/styles/ember.css'],
      lastUpdated: true,
      sidebar: [
        {
          label: 'Handbuch',
          items: [
            'handbuch/installation',
            'handbuch/erste-schritte',
            'handbuch/proxy',
            'handbuch/git',
            'handbuch/software',
            'handbuch/service-center',
            'handbuch/energie',
            'handbuch/system',
            'handbuch/ki',
            'handbuch/berechtigungen',
            'handbuch/einstellungen',
            'handbuch/team-konfiguration',
            'handbuch/updates',
            'handbuch/datenschutz',
            'handbuch/fehlerbehebung',
          ],
        },
        {
          label: 'Entwicklung',
          items: [
            'entwicklung/architektur',
            'entwicklung/bauen',
            'entwicklung/bridge-api',
            'entwicklung/neues-modul',
            'entwicklung/texte-und-umlaute',
            'entwicklung/releases',
          ],
        },
      ],
    }),
  ],
});
