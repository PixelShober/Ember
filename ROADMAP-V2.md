# Ember – Version 2

Gesammelte Ideen für die nächste große Version. Umgesetzt wird erst, wenn Version 1 im Alltag läuft.

## Interaktives Onboarding

Beim ersten Start führt Ember Schritt für Schritt durch die Einrichtung, statt alle Seiten auf einmal zu zeigen:

- Willkommen, Team-Konfiguration verbinden, px installieren, git-Identität, Software, Energie. Jeder Schritt ist eine eigene Ansicht, die seitlich hereingleitet (Slide-Übergänge).
- Jeder Schritt zeigt live, was gerade passiert, und lässt sich überspringen.
- Am Ende eine Zusammenfassung, was eingerichtet ist und was noch fehlt.
- Später erneut aufrufbar, etwa nach einem Rechnerwechsel.

Die Technik ist mit Version 1 schon da: Svelte bringt Übergänge und Animationen mit (`svelte/transition`, `svelte/animate`), die Seitenwechsel nutzen sie bereits. Das Onboarding lässt sich als reines Oberflächen-Update ausliefern, solange es keine neuen Bridge-Methoden braucht.
