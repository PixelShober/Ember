<script lang="ts" module>
  // Simple 24×24 stroke icons, drawn for Ember (no icon font needed, works offline).
  const paths: Record<string, string> = {
    home: 'M3.5 10.5 12 4l8.5 6.5V20a1 1 0 0 1-1 1h-5v-6h-5v6h-5a1 1 0 0 1-1-1z',
    proxy: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM3.6 9h16.8M3.6 15h16.8M12 3c2.4 2.5 3.6 5.5 3.6 9s-1.2 6.5-3.6 9c-2.4-2.5-3.6-5.5-3.6-9S9.6 5.5 12 3z',
    git: 'M6 3v12M18 9a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM6 21a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM18 9c0 6-6 6-12 9',
    software: 'M3.5 7.5 12 3l8.5 4.5v9L12 21l-8.5-4.5zM3.5 7.5 12 12l8.5-4.5M12 12v9',
    power: 'M13 2 4 14h7l-1 8 9-12h-7z',
    system: 'M12 3 4 6v6c0 4.5 3.4 8.3 8 9 4.6-.7 8-4.5 8-9V6zM9 12l2 2 4-4',
    ai: 'M12 3v3M12 18v3M3 12h3M18 12h3M7 7h10v10H7zM10 10h4v4h-4z',
    settings: 'M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12M20 18h0M14 4v4M8 10v4M16 16v4',
    log: 'M5 4h14v16H5zM8 8h8M8 12h8M8 16h5',
    check: 'M5 12.5 10 17 19 7.5',
    x: 'M6 6l12 12M18 6 6 18',
    alert: 'M12 3 2 20h20zM12 10v4M12 17.5v.01',
    info: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 11v6M12 7.5v.01',
    refresh: 'M20 12a8 8 0 1 1-2.3-5.7M20 4v5h-5',
    play: 'M7 4.5v15l12-7.5z',
    stop: 'M6 6h12v12H6z',
    external: 'M14 4h6v6M20 4l-9 9M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5',
    folder: 'M3 6a1 1 0 0 1 1-1h5l2 2h9a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1z',
    mail: 'M3 6h18v12H3zM3 7l9 6 9-6',
    copy: 'M8 8h12v12H8zM4 16V5a1 1 0 0 1 1-1h11',
    download: 'M12 4v11M7 10l5 5 5-5M5 20h14',
    trash: 'M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3',
    chevron: 'M9 6l6 6-6 6',
    bug: 'M8 8a4 4 0 0 1 8 0v6a4 4 0 0 1-8 0zM4 13h4M16 13h4M5 7l3 2M19 7l-3 2M5 19l3-2M19 19l-3-2M12 8v10',
    shield: 'M12 3 4 6v6c0 4.5 3.4 8.3 8 9 4.6-.7 8-4.5 8-9V6z',
    wake: 'M12 3v8M6.3 7.3a7.5 7.5 0 1 0 11.4 0',
    flame: 'M12 3c1.8 3.3 5.5 5 5.5 9.5A5.5 5.5 0 0 1 12 18a5.5 5.5 0 0 1-5.5-5.5c0-2.6 1.6-4.3 2.8-5.5.2 1.7.8 2.8 1.8 3.3-.5-2.6-.2-5.2.9-7.3z',
    sparkle: 'M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8zM19 16l.8 2.2L22 19l-2.2.8L19 22l-.8-2.2L16 19l2.2-.8z',
    plus: 'M12 5v14M5 12h14',
    report: 'M6 3h9l4 4v14H6zM14 3v5h5M9 13h7M9 17h5',
    user: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM4.5 20.5a7.5 7.5 0 0 1 15 0',
    gpu: 'M3 7h18v10H3zM7 17v3M17 17v3M8 12a2 2 0 1 0 4 0 2 2 0 0 0-4 0zM14 10h4M14 14h4',
    server: 'M4 4h16v6H4zM4 14h16v6H4zM8 7h.01M8 17h.01',
    terminal: 'M4 5h16v14H4zM8 10l3 2-3 2M13 15h4',
    undo: 'M9 14 4 9l5-5M4 9h11a5 5 0 0 1 0 10h-3',
  };
</script>

<script lang="ts">
  let { name, size = 18, label }: { name: string; size?: number; label?: string } = $props();
</script>

<svg
  width={size}
  height={size}
  viewBox="0 0 24 24"
  fill="none"
  stroke="currentColor"
  stroke-width="1.8"
  stroke-linecap="round"
  stroke-linejoin="round"
  role={label ? 'img' : undefined}
  aria-label={label}
  aria-hidden={label ? undefined : 'true'}
>
  <path d={paths[name] ?? paths.info} />
</svg>
