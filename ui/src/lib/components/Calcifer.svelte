<script lang="ts">
  // The tilted Calcifer marks things that are missing or not set up the way they should be.
  // It is decoration only: the text next to it always says what is wrong.
  import hint from '../../assets/calcifer-hint.png';

  let { size = 40 }: { size?: number } = $props();
</script>

<img class="calcifer" src={hint} alt="" width={size} height={Math.round((size * 215) / 227)} draggable="false" />

<style>
  .calcifer { flex: none; display: block; user-select: none; animation: flicker 2.8s ease-in-out infinite; transform-origin: 60% 70%; }
  @keyframes flicker {
    0%, 100% { transform: rotate(-3deg) scale(1); }
    50% { transform: rotate(3deg) scale(1.04); }
  }
  @media (prefers-reduced-motion: reduce) {
    .calcifer { animation: none; }
  }
</style>
