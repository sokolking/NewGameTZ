using Microsoft.Extensions.Configuration;

namespace BattleServer;

/// <summary>
/// Public onboarding page for alpha testers: RU/EN, animated layout. Route: <c>/alpha-guide</c>.
/// <para><b>Inline glossary (word in text):</b> inside any paragraph or list, wrap a word with
/// <c>&lt;span class="glossary-term" data-glossary-img="/alpha-guide/your.png" data-glossary-alt="Caption" tabindex="0" role="button"&gt;word&lt;/span&gt;</c>.
/// Put files under the server static root (e.g. <c>wwwroot/alpha-guide/</c>). Click opens full-screen image; Esc or backdrop closes.</para>
/// </summary>
public static class AlphaGuidePage
{
    private const string HtmlTemplate = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta http-equiv="Cache-Control" content="no-store, max-age=0" />
  <title>Hope — Alpha player guide</title>
  <style>
    :root {
      color-scheme: dark;
      --bg0: #0a0c10;
      --bg1: #12151c;
      --panel: #171a22;
      --border: #2b3140;
      --text: #e8ecf4;
      --muted: #9aa4b2;
      --accent: #7aa2ff;
      --accent2: #6dd3a0;
      --warn: #ffcc66;
      --glow: rgba(122, 162, 255, 0.35);
    }
    * { box-sizing: border-box; }
    html { scroll-behavior: smooth; }
    @media (prefers-reduced-motion: reduce) {
      html { scroll-behavior: auto; }
      *, *::before, *::after {
        animation-duration: 0.01ms !important;
        animation-iteration-count: 1 !important;
        transition-duration: 0.01ms !important;
      }
    }
    body {
      margin: 0;
      min-height: 100vh;
      background:
        linear-gradient(rgba(10, 12, 16, 0.82), rgba(10, 12, 16, 0.92)),
        url('/alpha-guide/background-hero.png') center center / cover no-repeat fixed,
        var(--bg0);
      color: var(--text);
      font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, sans-serif;
      line-height: 1.55;
      overflow-x: hidden;
    }
    .bg-blobs {
      position: fixed;
      inset: 0;
      pointer-events: none;
      z-index: 0;
      overflow: hidden;
    }
    .blob {
      position: absolute;
      border-radius: 50%;
      filter: blur(80px);
      opacity: 0.45;
      animation: blobFloat 18s ease-in-out infinite;
    }
    .blob-a { width: 420px; height: 420px; background: radial-gradient(circle, #1b3a6e 0%, transparent 70%); top: -120px; right: -80px; animation-delay: 0s; }
    .blob-b { width: 380px; height: 380px; background: radial-gradient(circle, #1e4d3a 0%, transparent 70%); bottom: 10%; left: -100px; animation-delay: -6s; }
    .blob-c { width: 300px; height: 300px; background: radial-gradient(circle, #4a2d5c 0%, transparent 70%); bottom: -80px; right: 20%; animation-delay: -12s; }
    @keyframes blobFloat {
      0%, 100% { transform: translate(0, 0) scale(1); }
      33% { transform: translate(30px, -20px) scale(1.05); }
      66% { transform: translate(-20px, 15px) scale(0.98); }
    }
    .wrap {
      position: relative;
      z-index: 1;
      max-width: 920px;
      margin: 0 auto;
      padding: 0 20px 64px;
    }
    .topbar {
      position: sticky;
      top: 0;
      z-index: 20;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      padding: 14px 0;
      background: linear-gradient(180deg, rgba(10,12,16,.95) 0%, rgba(10,12,16,.75) 70%, transparent 100%);
      backdrop-filter: blur(12px);
    }
    .brand {
      font-weight: 700;
      letter-spacing: 0.06em;
      font-size: 13px;
      text-transform: uppercase;
      color: var(--accent);
    }
    .lang-toggle {
      display: flex;
      border: 1px solid var(--border);
      border-radius: 999px;
      overflow: hidden;
      background: var(--panel);
    }
    .top-actions {
      display: flex;
      align-items: center;
      gap: 10px;
    }
    .music-toggle {
      border: 1px solid var(--border);
      background: var(--panel);
      color: var(--muted);
      border-radius: 999px;
      padding: 8px 12px;
      font-size: 12px;
      cursor: pointer;
      transition: all 0.2s ease;
      min-width: 92px;
    }
    .music-toggle:hover { color: var(--text); border-color: #46506a; }
    .music-toggle.active {
      color: var(--accent2);
      border-color: rgba(109, 211, 160, 0.45);
      box-shadow: 0 0 0 2px rgba(109, 211, 160, 0.15);
    }
    .lang-toggle button {
      border: none;
      background: transparent;
      color: var(--muted);
      padding: 8px 16px;
      font-size: 13px;
      cursor: pointer;
      font-family: inherit;
    }
    .lang-toggle button:hover { color: var(--text); }
    .lang-toggle button.active {
      background: rgba(122, 162, 255, 0.15);
      color: var(--accent);
      font-weight: 600;
    }
    .lang-en, .lang-ru { display: none; }
    html[lang="en"] .lang-en { display: block; }
    html[lang="ru"] .lang-ru { display: block; }
    html[lang="en"] .lang-en.inline, html[lang="ru"] .lang-ru.inline { display: inline; }
    html[lang="en"] span.lang-en.inline, html[lang="ru"] span.lang-ru.inline { display: inline; }
    /* Base .cheat-grid must not set display:grid — it overrode .lang-* and showed both languages. */
    .cheat-grid.lang-en,
    .cheat-grid.lang-ru { display: none !important; }
    html[lang="en"] .cheat-grid.lang-en,
    html[lang="ru"] .cheat-grid.lang-ru { display: grid !important; }
    .hero {
      padding: 32px 0 48px;
      text-align: center;
    }
    .hero-badge {
      display: inline-block;
      padding: 6px 14px;
      border-radius: 999px;
      border: 1px solid var(--border);
      font-size: 12px;
      color: var(--muted);
      margin-bottom: 16px;
      animation: fadeInUp 0.8s ease both;
    }
    .hero h1 {
      margin: 0 0 16px;
      font-size: clamp(1.75rem, 4vw, 2.35rem);
      font-weight: 800;
      letter-spacing: -0.02em;
      line-height: 1.2;
      background: linear-gradient(135deg, #fff 0%, #a8b8d8 100%);
      -webkit-background-clip: text;
      background-clip: text;
      color: transparent;
      animation: fadeInUp 0.8s ease 0.1s both;
    }
    .hero p.lead {
      margin: 0 auto;
      max-width: 640px;
      font-size: 1.05rem;
      color: var(--muted);
      animation: fadeInUp 0.8s ease 0.2s both;
    }
    @keyframes fadeInUp {
      from { opacity: 0; transform: translateY(16px); }
      to { opacity: 1; transform: translateY(0); }
    }
    .hero-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
      gap: 12px;
      margin-top: 32px;
      max-width: 560px;
      margin-left: auto;
      margin-right: auto;
    }
    .stat-pill {
      background: var(--panel);
      border: 1px solid var(--border);
      border-radius: 12px;
      padding: 14px 12px;
      font-size: 12px;
      color: var(--muted);
      transition: border-color 0.2s, transform 0.2s;
    }
    .stat-pill:hover { border-color: #46506a; transform: translateY(-2px); }
    .stat-pill strong { display: block; font-size: 1.25rem; color: var(--accent2); margin-bottom: 4px; }
    section {
      margin-bottom: 40px;
    }
    .section-title {
      font-size: 1.35rem;
      font-weight: 700;
      margin: 0 0 8px;
      padding-bottom: 10px;
      border-bottom: 1px solid var(--border);
    }
    .section-sub {
      margin: 0 0 20px;
      color: var(--muted);
      font-size: 14px;
    }
    .card {
      background: var(--panel);
      border: 1px solid var(--border);
      border-radius: 14px;
      padding: 18px 20px;
      margin-bottom: 12px;
    }
    .card h3 { margin: 0 0 10px; font-size: 1rem; color: var(--accent); }
    .card ul { margin: 0; padding-left: 1.2em; }
    .card li { margin: 6px 0; }
    .reveal {
      opacity: 0;
      transform: translateY(20px);
      transition: opacity 0.55s ease, transform 0.55s ease;
    }
    .reveal.visible {
      opacity: 1;
      transform: translateY(0);
    }
    .timeline {
      position: relative;
      padding-left: 28px;
      border-left: 2px solid var(--border);
      margin-left: 8px;
    }
    .timeline-step {
      position: relative;
      padding-bottom: 22px;
    }
    .timeline-step:last-child { padding-bottom: 0; }
    .timeline-step::before {
      content: "";
      position: absolute;
      left: -35px;
      top: 4px;
      width: 12px;
      height: 12px;
      border-radius: 50%;
      background: var(--accent);
      box-shadow: 0 0 0 4px rgba(122, 162, 255, 0.2);
      animation: pulseDot 2.5s ease infinite;
    }
    .timeline-step:nth-child(2)::before { animation-delay: 0.4s; }
    .timeline-step:nth-child(3)::before { animation-delay: 0.8s; }
    .timeline-step:nth-child(4)::before { animation-delay: 1.2s; }
    @keyframes pulseDot {
      0%, 100% { box-shadow: 0 0 0 4px rgba(122, 162, 255, 0.2); }
      50% { box-shadow: 0 0 0 8px rgba(122, 162, 255, 0.08); }
    }
    .timeline-step h4 { margin: 0 0 6px; font-size: 15px; }
    .timeline-step p { margin: 0; font-size: 14px; color: var(--muted); }
    .kbd-row {
      display: flex;
      flex-wrap: wrap;
      gap: 10px;
      align-items: center;
      margin: 10px 0;
    }
    kbd {
      display: inline-block;
      padding: 6px 10px;
      border-radius: 8px;
      border: 1px solid var(--border);
      background: #11151d;
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
      font-size: 12px;
      color: var(--text);
      box-shadow: 0 2px 0 #0a0c10;
      animation: kbdPop 0.35s ease both;
    }
    .kbd-row:nth-child(1) kbd { animation-delay: 0.05s; }
    .kbd-row:nth-child(2) kbd { animation-delay: 0.1s; }
    @keyframes kbdPop {
      from { opacity: 0; transform: translateY(6px); }
      to { opacity: 1; transform: translateY(0); }
    }
    .cheat-grid {
      grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
      gap: 12px;
    }
    .cheat-item {
      display: flex;
      gap: 12px;
      align-items: flex-start;
      padding: 14px;
      border-radius: 12px;
      border: 1px solid var(--border);
      background: rgba(23, 26, 34, 0.6);
      transition: border-color 0.2s;
    }
    .cheat-item:hover { border-color: rgba(122, 162, 255, 0.4); }
    .cheat-item .keys { flex-shrink: 0; min-width: 88px; }
    .ui-tour {
      background: linear-gradient(180deg, rgba(23,26,34,0.92) 0%, rgba(17,21,29,0.96) 100%);
      border: 1px solid var(--border);
      border-radius: 16px;
      padding: 14px;
      overflow: hidden;
    }
    .ui-tour-toolbar {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
      align-items: center;
      margin-bottom: 12px;
    }
    .ui-tour-btn {
      border: 1px solid var(--border);
      background: #11151d;
      color: var(--muted);
      border-radius: 10px;
      padding: 8px 12px;
      font-size: 13px;
      cursor: pointer;
      transition: all 0.2s ease;
    }
    .ui-tour-btn:hover { color: var(--text); border-color: #46506a; }
    .ui-tour-btn.active {
      color: var(--text);
      border-color: rgba(122, 162, 255, 0.65);
      background: linear-gradient(180deg, rgba(122,162,255,0.2) 0%, rgba(122,162,255,0.08) 100%);
      box-shadow: 0 0 0 2px rgba(122,162,255,0.18);
    }
    .ui-tour-stage {
      position: relative;
      border-radius: 12px;
      overflow: hidden;
      border: 1px solid rgba(122,162,255,0.25);
      background: #0d0f14;
      aspect-ratio: 16 / 10;
    }
    .ui-tour-img {
      position: absolute;
      inset: 0;
      width: 100%;
      height: 100%;
      object-fit: cover;
      opacity: 0;
      transform: scale(1.03);
      pointer-events: none;
      transition: opacity 0.45s ease, transform 0.6s ease;
    }
    .ui-tour-img.active {
      opacity: 1;
      transform: scale(1);
      pointer-events: auto;
      z-index: 2;
    }
    .ui-tour-caption {
      margin-top: 10px;
      font-size: 13px;
      color: var(--muted);
      min-height: 20px;
    }
    .faq details {
      border: 1px solid var(--border);
      border-radius: 10px;
      padding: 12px 16px;
      margin-bottom: 8px;
      background: var(--panel);
    }
    .faq summary {
      cursor: pointer;
      font-weight: 600;
      list-style: none;
    }
    .faq summary::-webkit-details-marker { display: none; }
    .faq details[open] summary { color: var(--accent); }
    .faq p { margin: 10px 0 0; color: var(--muted); font-size: 14px; }
    footer {
      margin-top: 48px;
      padding-top: 24px;
      border-top: 1px solid var(--border);
      font-size: 12px;
      color: var(--muted);
      text-align: center;
    }
    a { color: var(--accent); }
    .discord-cta {
      margin-top: 24px;
      padding: 18px 20px;
      border-radius: 14px;
      border: 1px solid rgba(88, 101, 242, 0.45);
      background: linear-gradient(135deg, rgba(88, 101, 242, 0.12) 0%, rgba(23, 26, 34, 0.9) 100%);
      text-align: center;
      animation: fadeInUp 0.85s ease 0.25s both;
    }
    .discord-cta p { margin: 0 0 12px; color: var(--muted); font-size: 14px; }
    .discord-cta a.btn-discord {
      display: inline-flex;
      align-items: center;
      gap: 10px;
      padding: 12px 22px;
      border-radius: 10px;
      background: #5865f2;
      color: #fff !important;
      text-decoration: none;
      font-weight: 600;
      font-size: 15px;
      transition: transform 0.2s, box-shadow 0.2s;
      box-shadow: 0 4px 20px rgba(88, 101, 242, 0.35);
    }
    .discord-cta a.btn-discord:hover { transform: translateY(-2px); box-shadow: 0 8px 28px rgba(88, 101, 242, 0.45); }
    .client-downloads { margin-top: 20px; text-align: center; }
    .client-downloads-title { margin: 0 0 12px; color: var(--muted); font-size: 14px; }
    .client-downloads-btns {
      display: flex;
      flex-wrap: wrap;
      gap: 10px;
      justify-content: center;
      align-items: center;
    }
    .btn-client {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      padding: 11px 20px;
      border-radius: 10px;
      text-decoration: none !important;
      font-weight: 600;
      font-size: 14px;
      transition: transform 0.2s, box-shadow 0.2s;
      min-width: 140px;
    }
    .btn-client-mac {
      background: linear-gradient(180deg, #4a4a4e 0%, #2d2d30 100%);
      color: #f5f5f7 !important;
      border: 1px solid #5c5c60;
      box-shadow: 0 2px 12px rgba(0, 0, 0, 0.35);
    }
    .btn-client-mac:hover { transform: translateY(-2px); box-shadow: 0 6px 20px rgba(0, 0, 0, 0.45); }
    .btn-client-win {
      background: linear-gradient(180deg, #1a8cff 0%, #0078d4 100%);
      color: #fff !important;
      border: 1px solid #3aa0ff;
      box-shadow: 0 2px 12px rgba(0, 120, 212, 0.35);
    }
    .btn-client-win:hover { transform: translateY(-2px); box-shadow: 0 6px 22px rgba(0, 120, 212, 0.45); }
    /* Inline word in running text — use <span class="glossary-term" ...>word</span> inside <p>, <li>, etc. */
    .glossary-term {
      display: inline;
      vertical-align: baseline;
      cursor: pointer;
      border-bottom: 1px dashed rgba(122, 162, 255, 0.75);
      background: none;
      color: inherit;
      font: inherit;
      line-height: inherit;
      padding: 0;
      margin: 0;
      text-align: inherit;
      box-shadow: none;
      border-radius: 0;
      -webkit-tap-highlight-color: transparent;
    }
    button.glossary-term {
      appearance: none;
      border-left: none;
      border-right: none;
      border-top: none;
    }
    .glossary-term:hover { color: var(--accent); border-bottom-color: var(--accent); }
    .glossary-term:focus-visible {
      outline: 2px solid var(--accent);
      outline-offset: 2px;
      border-radius: 2px;
    }
    #glossaryLightbox {
      position: fixed;
      inset: 0;
      z-index: 10000;
      display: none;
      align-items: center;
      justify-content: center;
      padding: 24px;
      background: rgba(0, 0, 0, 0.92);
      cursor: zoom-out;
    }
    #glossaryLightbox.is-open { display: flex; }
    #glossaryLightbox img {
      max-width: 100%;
      max-height: 100%;
      width: auto;
      height: auto;
      object-fit: contain;
      border-radius: 8px;
      box-shadow: 0 8px 48px rgba(0, 0, 0, 0.6);
      pointer-events: none;
    }
    #glossaryLightbox .glossary-lightbox-hint {
      position: absolute;
      bottom: 16px;
      left: 50%;
      transform: translateX(-50%);
      font-size: 12px;
      color: var(--muted);
      pointer-events: none;
    }
  </style>
</head>
<body>
  <div class="bg-blobs" aria-hidden="true">
    <div class="blob blob-a"></div>
    <div class="blob blob-b"></div>
    <div class="blob blob-c"></div>
  </div>
  <div class="wrap">
    <header class="topbar">
      <div class="brand">Hope</div>
      <div class="top-actions">
        <button type="button" id="musicToggle" class="music-toggle">Music: Off</button>
        <div class="lang-toggle" role="group" aria-label="Language">
          <button type="button" id="btn-en" class="active" data-set-lang="en">EN</button>
          <button type="button" id="btn-ru" data-set-lang="ru">RU</button>
        </div>
      </div>
    </header>

    <section class="hero">
      <div class="lang-en">
        <div class="hero-badge">Alpha test — player guide</div>
        <h1>Welcome to Hope</h1>
        <p class="lead">A tactical hex-grid battle: plan your turn, spend Action Points (AP), and resolve the round together with your opponent. This page explains the loop, core mechanics, and controls.</p>
        <div class="hero-grid">
          <div class="stat-pill"><strong>Hex</strong> movement &amp; range</div>
          <div class="stat-pill"><strong>AP</strong> plan &amp; queue</div>
          <div class="stat-pill"><strong>WS</strong> live sync</div>
        </div>
        <div class="client-downloads">
          <p class="client-downloads-title">Download the game client</p>
          <div class="client-downloads-btns">
            <a class="btn-client btn-client-mac" href="__HDL_MAC__">macOS (.dmg)</a>
            <a class="btn-client btn-client-win" href="__HDL_WIN__">Windows (.zip)</a>
          </div>
        </div>
        <div class="discord-cta">
          <p>Join the community — feedback, patches, and alpha discussion.</p>
          <a class="btn-discord" href="https://discord.gg/85WsYHHf" target="_blank" rel="noopener noreferrer">Discord — HopeAlpha</a>
        </div>
      </div>
      <div class="lang-ru">
        <div class="hero-badge">Альфа-тест — гайд для игрока</div>
        <h1>Добро пожаловать в Hope</h1>
        <p class="lead">Тактический бой на гексах: планируйте ход, тратьте очки действия (ОД) и одновременно с соперником разрешайте раунд. Здесь — цикл боя, механики и управление.</p>
        <div class="hero-grid">
          <div class="stat-pill"><strong>Гексы</strong> движение и дальность</div>
          <div class="stat-pill"><strong>ОД</strong> план и очередь</div>
          <div class="stat-pill"><strong>WS</strong> синхронизация</div>
        </div>
        <div class="client-downloads">
          <p class="client-downloads-title">Скачать клиент игры</p>
          <div class="client-downloads-btns">
            <a class="btn-client btn-client-mac" href="__HDL_MAC__">macOS (.dmg)</a>
            <a class="btn-client btn-client-win" href="__HDL_WIN__">Windows (.zip)</a>
          </div>
        </div>
        <div class="discord-cta">
          <p>Сообщество — обратная связь, обновления и обсуждение альфы.</p>
          <a class="btn-discord" href="https://discord.gg/85WsYHHf" target="_blank" rel="noopener noreferrer">Discord — HopeAlpha</a>
        </div>
      </div>
    </section>

    <section class="reveal" data-reveal>
      <h2 class="section-title lang-en">What you are playing</h2>
      <h2 class="section-title lang-ru">Во что вы играете</h2>
      <p class="section-sub lang-en">Hope is an online turn-based combat game on a hex map. You and other units (players or AI mobs) submit actions for each round; the server resolves movement, shooting, cover, and damage.</p>
      <p class="section-sub lang-ru">Hope — онлайн пошаговый бой на гексагональной карте. Вы и другие юниты (игроки или ИИ) отправляете действия на раунд; сервер считает движение, стрельбу, укрытия и урон.</p>
      <div class="card lang-en">
        <h3>Alpha expectations</h3>
        <ul>
          <li>Balance, UI, and content may change between builds.</li>
          <li>Report crashes, desyncs, and confusing UX — it helps a lot.</li>
          <li>Disconnects do not automatically end a battle; only victory/defeat or surrender (Esc menu) finishes the session as designed.</li>
        </ul>
      </div>
      <div class="card lang-ru">
        <h3>Ожидания от альфы</h3>
        <ul>
          <li>Баланс, интерфейс и контент могут меняться между сборками.</li>
          <li>Сообщайте о вылетах, рассинхронах и непонятном UX — это очень помогает.</li>
          <li>Разрыв связи сам по себе не завершает бой; сессия по задумке заканчивается победой/поражением или побегом.</li>
        </ul>
      </div>
    </section>

    <section class="reveal" data-reveal>
      <h2 class="section-title lang-en">One round, step by step</h2>
      <h2 class="section-title lang-ru">Один раунд по шагам</h2>
      <div class="lang-en timeline">
        <div class="timeline-step">
          <h4>Plan</h4>
          <p>Move on the grid, change posture, attack, reload, use items, or end your turn — all spending AP within the round timer.</p>
        </div>
        <div class="timeline-step">
          <h4>Submit</h4>
          <p>Press End Turn (or the shortcut). Your plan is sent to the server over the battle WebSocket.</p>
        </div>
        <div class="timeline-step">
          <h4>Resolve</h4>
          <p>When the round closes, you receive the turn result: hits, damage, posture, magazine, map updates, zone shrink, etc.</p>
        </div>
        <div class="timeline-step">
          <h4>Next round</h4>
          <p>A new round starts with refreshed AP and a new deadline. Repeat until battle end or escape.</p>
        </div>
      </div>
      <div class="lang-ru timeline">
        <div class="timeline-step">
          <h4>План</h4>
          <p>Движение по сетке, смена позы, атака, перезарядка, предметы или завершение хода — всё за счёт ОД в лимите времени раунда.</p>
        </div>
        <div class="timeline-step">
          <h4>Отправка</h4>
          <p>Нажмите «конец хода» (или горячую клавишу). План уходит на сервер по WebSocket боя.</p>
        </div>
        <div class="timeline-step">
          <h4>Расчёт</h4>
          <p>После закрытия раунда приходит результат: попадания, урон, поза, магазин, карта, сужение зоны и т.д.</p>
        </div>
        <div class="timeline-step">
          <h4>Следующий раунд</h4>
          <p>Новый раунд с обновлёнными ОД и дедлайном. Повторяйте до конца боя или побега.</p>
        </div>
      </div>
    </section>

    <section class="reveal" data-reveal>
      <h2 class="section-title lang-en">Core mechanics</h2>
      <h2 class="section-title lang-ru">Основные механики</h2>
      <div class="card lang-en">
        <h3>Action Points (AP)</h3>
        <ul>
          <li>Each action (move step, posture change, attack, reload, item use) costs AP. Running and sitting/hiding modify move cost per hex.</li>
          <li>You can undo the last queued action with Step Back when allowed.</li>
          <li>“Skip AP” lets you burn AP to wait in place (opens a dialog to enter the amount).</li>
        </ul>
      </div>
      <div class="card lang-ru">
        <h3>Очки действия (ОД)</h3>
        <ul>
          <li>Каждое действие (шаг, смена позы, атака, перезарядка, предмет) стоит ОД. Бег и сидение/укрытие меняют стоимость шага по гексам.</li>
          <li>Можно отменить последнее действие в очереди кнопкой «шаг назад», если это разрешено.</li>
          <li>«Пропуск ОД» — потратить ОД на ожидание на месте (откроется ввод числа).</li>
        </ul>
      </div>
      <div class="card lang-en">
        <h3>Postures</h3>
        <ul>
          <li><strong>Walk / Run / Sit / Hide</strong> — affects movement cost and combat profile. You must be able to move (e.g. leave Hide) before pathing.</li>
          <li>Changing posture costs AP (queued like other actions).</li>
        </ul>
      </div>
      <div class="card lang-ru">
        <h3>Позы</h3>
        <ul>
          <li><strong>Ходьба / бег / сидение / укрытие</strong> — влияют на стоимость движения и бой. Перед путём нужно иметь возможность двигаться (например, выйти из укрытия).</li>
          <li>Смена позы стоит ОД и ставится в очередь как остальные действия.</li>
        </ul>
      </div>
      <div class="card lang-en">
        <h3>Combat &amp; gear</h3>
        <ul>
          <li>Weapon <strong>range</strong>, <strong>damage</strong>, <strong>attack AP</strong>, magazine and reload cost come from the server.</li>
          <li>Hit chance depends on distance, cover (trees, rocks), target posture, accuracy, and weapon spread (tightness).</li>
          <li>You can switch equipped items from inventory; equipping in battle queues an action.</li>
        </ul>
      </div>
      <div class="card lang-ru">
        <h3>Бой и снаряжение</h3>
        <ul>
          <li><strong>Дальность</strong>, <strong>урон</strong>, <strong>стоимость атаки в ОД</strong>, магазин и цена перезарядки задаются сервером.</li>
          <li>Шанс попадания зависит от дистанции, укрытий (деревья, камни), позы цели, меткости и разброса оружия.</li>
          <li>Смена предметов — через инвентарь; экипировка в бою тоже ставится в очередь действий.</li>
        </ul>
      </div>
      <div class="card lang-en">
        <h3>Escape ring</h3>
        <ul>
          <li>Gray border hexes mark the escape edge. Double-click movement that first steps from inside onto that ring opens a confirmation dialog.</li>
          <li>After confirming flee, the server runs the escape channel; ending the battle by escape is different from disconnecting.</li>
        </ul>
      </div>
      <div class="card lang-ru">
        <h3>Кольцо побега</h3>
        <ul>
          <li>Серая кайма гексов — край для побега. Двойной клик по пути, который впервые выходит изнутри на это кольцо, открывает подтверждение.</li>
          <li>После подтверждения сервер ведёт канал побега; побег — не то же самое, что обрыв связи.</li>
        </ul>
      </div>
    </section>

    <section class="reveal" data-reveal>
      <h2 class="section-title lang-en">Controls (PC)</h2>
      <h2 class="section-title lang-ru">Управление (ПК)</h2>
      <p class="section-sub lang-en">Mouse and keyboard bindings used by the current client. Pan/zoom defaults may match your scene (e.g. pan after zooming in).</p>
      <p class="section-sub lang-ru">Мышь и клавиши из текущего клиента. Панорама/зум могут соответствовать сцене (например, перетаскивание после приближения).</p>
      <div class="cheat-grid lang-en">
        <div class="cheat-item">
          <div class="keys"><kbd>Dbl-click</kbd></div>
          <div>Move — double-click a hex to walk/run along the planned path (AP permitting).</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>Ctrl</kbd> + <kbd>LMB</kbd></div>
          <div>Hex shot — attack the aimed hex (enemy, wall cell, etc.). Hold <kbd>Shift</kbd> while clicking to queue repeated shots where supported.</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>Hold LMB</kbd></div>
          <div>On an enemy — body-part silhouette; release to confirm. On yourself with a medicine item active — self-use targeting.</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>RMB</kbd></div>
          <div>Inspect — open the unit card for a unit under cursor (or your hex).</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>Ctrl</kbd> (hold)</div>
          <div>Show attack range outline while aiming from top-down view.</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>1</kbd>–<kbd>4</kbd></div>
          <div>Posture: Walk, Run, Sit, Hide (same as UI buttons; disabled while typing in a dialog field).</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>D</kbd> / <kbd>E</kbd></div>
          <div>End turn — immediate vs animated submit (online: sends your queued plan).</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>R</kbd></div>
          <div>Queue reload (AP cost from your weapon).</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>Wheel</kbd></div>
          <div>Zoom the tactical camera in and out.</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>Drag</kbd></div>
          <div>Pan the map when zoomed in (exact mouse button depends on camera settings).</div>
        </div>
      </div>
      <div class="cheat-grid lang-ru">
        <div class="cheat-item">
          <div class="keys"><kbd>Dbl-click</kbd></div>
          <div>Движение — двойной клик по гексу: путь и шаги с учётом ОД.</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>Ctrl</kbd> + <kbd>ЛКМ</kbd></div>
          <div>Выстрел по гексу прицела. С <kbd>Shift</kbd> — повторные выстрелы, где поддерживается.</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>Удерж. ЛКМ</kbd></div>
          <div>По врагу — силуэт частей тела; отпускание подтверждает. По себе с активным медпредметом — применение на себя.</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>ПКМ</kbd></div>
          <div>Осмотр — карточка юнита под курсором (или ваш гекс).</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>Ctrl</kbd></div>
          <div>В виде сверху — контур дальности атаки при прицеливании.</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>1</kbd>–<kbd>4</kbd></div>
          <div>Позы: ходьба, бег, сидение, укрытие (как кнопки UI; не в поле ввода диалога).</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>D</kbd> / <kbd>E</kbd></div>
          <div>Конец хода — сразу или с анимацией (онлайн: отправка очереди).</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>R</kbd></div>
          <div>Перезарядка в очередь (стоимость с оружия).</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>Колёсико</kbd></div>
          <div>Зум тактической камеры.</div>
        </div>
        <div class="cheat-item">
          <div class="keys"><kbd>Drag</kbd></div>
          <div>Панорама карты при приближении (кнопка мыши задаётся в камере).</div>
        </div>
      </div>
    </section>

    <section class="reveal" data-reveal>
      <h2 class="section-title lang-en">Quick start checklist</h2>
      <h2 class="section-title lang-ru">Быстрый старт</h2>
      <div class="card lang-en">
        <ul>
          <li>Glance at the minimap timer and your remaining AP each round.</li>
          <li>Set posture before moving long distances (run vs walk trade-offs).</li>
          <li>Use Ctrl + click or hold-target for ranged fights; right-click to inspect unknown units.</li>
          <li>Reload before you empty the magazine in a pinch; watch reload AP.</li>
          <li>End turn early if your plan is complete — don’t wait for the timer.</li>
        </ul>
      </div>
      <div class="card lang-ru">
        <ul>
          <li>Смотрите таймер на миникарте и остаток ОД каждый раунд.</li>
          <li>Выбирайте позу до длинного пути (бег vs ходьба).</li>
          <li>На дистанции — Ctrl+клик или удержание по силуэту; по незнакомым юнитам — ПКМ для карточки.</li>
          <li>Следите за магазином и перезарядкой, учитывайте ОД на reload.</li>
          <li>Завершайте ход, если план готов — не обязательно ждать таймер.</li>
        </ul>
      </div>
    </section>

    <section class="reveal" data-reveal>
      <h2 class="section-title lang-en">Interface walkthrough</h2>
      <h2 class="section-title lang-ru">Пояснение интерфейса</h2>
      <p class="section-sub lang-en">Switch between BaseView (top-down) and 3rdPView to understand how the battle UI is placed during gameplay.</p>
      <p class="section-sub lang-ru">Переключайте BaseView (вид сверху) и 3rdPView (вид от третьего лица), чтобы быстрее разобраться в расположении элементов интерфейса боя.</p>
      <div class="ui-tour">
        <div class="ui-tour-toolbar">
          <button type="button" class="ui-tour-btn active" data-ui-view="base">BaseView</button>
          <button type="button" class="ui-tour-btn" data-ui-view="third">3rdPView</button>
        </div>
        <div class="ui-tour-stage" aria-live="polite">
          <img class="ui-tour-img active" data-ui-image="base" src="/alpha-guide/BaseView.png" alt="BaseView interface explanation image" />
          <img class="ui-tour-img" data-ui-image="third" src="/alpha-guide/3rdPView.png" alt="3rdPView interface explanation image" />
        </div>
        <div id="uiTourCaption" class="ui-tour-caption"></div>
      </div>
    </section>

    <section class="reveal faq" data-reveal>
      <h2 class="section-title lang-en">FAQ</h2>
      <h2 class="section-title lang-ru">Частые вопросы</h2>
      <div class="lang-en">
        <details><summary>Why did my action fail?</summary><p>Not enough AP, out of range, blocked path, wrong round, or server validation. Check the on-screen message and combat log.</p></details>
        <details><summary>What if I disconnect?</summary><p>The battle may continue; log back in — unfinished battles can be resumed when the server still holds the room.</p></details>
        <details><summary>How do I surrender?</summary><p>Use the surrender control from the Esc menu (not simply closing the game).</p></details>
      </div>
      <div class="lang-ru">
        <details><summary>Почему действие не применилось?</summary><p>Не хватило ОД, дальность, блок пути, не тот раунд или проверка сервера. Смотрите подсказку и журнал боя.</p></details>
        <details><summary>Что если пропал интернет?</summary><p>Бой может продолжаться; зайдите снова — незавершённые бои можно возобновить, пока комната на сервере жива.</p></details>
        <details><summary>Как сдаться?</summary><p>Через линии побега</p></details>
      </div>
    </section>

    <footer>
      <p class="lang-en">Hope — alpha documentation. For server operators: internal tools remain on other routes.</p>
      <p class="lang-ru">Hope — документация для альфы. Для операторов: внутренние инструменты на других URL.</p>
    </footer>
  </div>
  <!--
    GLOSSARY — слово ВНУТРИ текста (абзац, список, заголовок):
    1) Положите картинку в статику сервера, напр. wwwroot/alpha-guide/ap.png → URL /alpha-guide/ap.png
    2) Оборачиваете только слово(а), без разрыва смысла:
       <p>... <span class="glossary-term" data-glossary-img="/alpha-guide/foo.png" data-glossary-alt="Подпись" tabindex="0" role="button">термин</span> ...</p>
    3) Клик — полноэкранная картинка; Esc или клик по затемнению — закрыть.
       data-glossary-alt попадает в alt у картинки (доступность).
    Пример в этом файле: lead-параграфы EN/RU с термином AP/ОД → добавьте ap.png или поменяйте путь.
  -->
  <div id="glossaryLightbox" role="dialog" aria-modal="true" aria-label="Image" aria-hidden="true">
    <img id="glossaryLightboxImg" alt="" />
    <span class="glossary-lightbox-hint lang-en">Click anywhere or press Esc to close</span>
    <span class="glossary-lightbox-hint lang-ru">Клик или Esc — закрыть</span>
  </div>
  <audio id="pageMusic" src="/alpha-guide/menuMusic.mp3" loop preload="auto" playsinline></audio>
  <script>
    (function () {
      var STORAGE_KEY = 'hopeAlphaGuideLang';
      function setLang(lang) {
        if (lang !== 'en' && lang !== 'ru') lang = 'en';
        document.documentElement.lang = lang;
        var btnEn = document.getElementById('btn-en');
        var btnRu = document.getElementById('btn-ru');
        if (btnEn) btnEn.classList.toggle('active', lang === 'en');
        if (btnRu) btnRu.classList.toggle('active', lang === 'ru');
        try { localStorage.setItem(STORAGE_KEY, lang); } catch (e) {}
      }
      var saved = null;
      try { saved = localStorage.getItem(STORAGE_KEY); } catch (e) {}
      if (saved === 'ru' || saved === 'en') setLang(saved);
      else setLang('en');
      document.getElementById('btn-en').addEventListener('click', function () { setLang('en'); });
      document.getElementById('btn-ru').addEventListener('click', function () { setLang('ru'); });

      var musicEl = document.getElementById('pageMusic');
      var musicBtn = document.getElementById('musicToggle');
      function tryStartMusic() {
        if (!musicEl || musicEl.muted) return;
        var p = musicEl.play();
        if (p && typeof p.catch === 'function') p.catch(function () {});
      }
      function updateMusicButton(isMuted) {
        if (!musicBtn) return;
        var lang = document.documentElement.lang === 'ru' ? 'ru' : 'en';
        if (lang === 'ru')
          musicBtn.textContent = isMuted ? 'Музыка: выкл' : 'Музыка: вкл';
        else
          musicBtn.textContent = isMuted ? 'Music: Off' : 'Music: On';
        musicBtn.classList.toggle('active', !isMuted);
      }
      function setMusicMuted(isMuted) {
        if (!musicEl) return;
        musicEl.muted = !!isMuted;
        if (!isMuted) {
          tryStartMusic();
        } else {
          musicEl.pause();
        }
        updateMusicButton(isMuted);
      }
      setMusicMuted(true);
      if (musicBtn) {
        musicBtn.addEventListener('click', function () {
          var nowMuted = !(musicEl && musicEl.muted);
          setMusicMuted(nowMuted);
        });
      }

      var reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
      if (!reduce) {
        var nodes = document.querySelectorAll('[data-reveal]');
        var io = new IntersectionObserver(function (entries) {
          entries.forEach(function (e) {
            if (e.isIntersecting) e.target.classList.add('visible');
          });
        }, { rootMargin: '0px 0px -40px 0px', threshold: 0.08 });
        nodes.forEach(function (n) { io.observe(n); });
      } else {
        document.querySelectorAll('[data-reveal]').forEach(function (n) { n.classList.add('visible'); });
      }

      var uiButtons = document.querySelectorAll('[data-ui-view]');
      var uiImages = document.querySelectorAll('[data-ui-image]');
      var uiCaption = document.getElementById('uiTourCaption');
      var uiCaptions = {
        en: {
          base: 'BaseView: tactical top-down readout with full map context.',
          third: '3rdPView: immersive perspective for positioning and line-of-sight.'
        },
        ru: {
          base: 'BaseView: тактический вид сверху с полным обзором карты.',
          third: '3rdPView: более погружающий вид для оценки позиции и линии огня.'
        }
      };
      function setUiView(view) {
        uiButtons.forEach(function (b) { b.classList.toggle('active', b.getAttribute('data-ui-view') === view); });
        uiImages.forEach(function (img) { img.classList.toggle('active', img.getAttribute('data-ui-image') === view); });
        var lang = document.documentElement.lang === 'ru' ? 'ru' : 'en';
        if (uiCaption && uiCaptions[lang] && uiCaptions[lang][view])
          uiCaption.textContent = uiCaptions[lang][view];
      }
      uiButtons.forEach(function (btn) {
        btn.addEventListener('click', function () { setUiView(btn.getAttribute('data-ui-view')); });
      });
      setUiView('base');
      uiImages.forEach(function (img) {
        img.style.cursor = 'zoom-in';
        img.addEventListener('click', function () {
          openGlossary(img.getAttribute('src') || '', img.getAttribute('alt') || '');
        });
      });
      document.getElementById('btn-en').addEventListener('click', function () {
        var active = document.querySelector('.ui-tour-btn.active');
        setUiView(active ? active.getAttribute('data-ui-view') : 'base');
        updateMusicButton(musicEl ? musicEl.muted : true);
      });
      document.getElementById('btn-ru').addEventListener('click', function () {
        var active = document.querySelector('.ui-tour-btn.active');
        setUiView(active ? active.getAttribute('data-ui-view') : 'base');
        updateMusicButton(musicEl ? musicEl.muted : true);
      });

      var lb = document.getElementById('glossaryLightbox');
      var lbImg = document.getElementById('glossaryLightboxImg');
      function openGlossary(src, alt) {
        if (!lb || !lbImg || !src) return;
        lbImg.src = src;
        lbImg.alt = alt || '';
        lb.classList.add('is-open');
        lb.setAttribute('aria-hidden', 'false');
        document.body.style.overflow = 'hidden';
      }
      function closeGlossary() {
        if (!lb || !lbImg) return;
        lb.classList.remove('is-open');
        lb.setAttribute('aria-hidden', 'true');
        lbImg.removeAttribute('src');
        lbImg.alt = '';
        document.body.style.overflow = '';
      }
      document.addEventListener('click', function (ev) {
        var t = ev.target.closest('.glossary-term');
        if (!t || !t.getAttribute('data-glossary-img')) return;
        ev.preventDefault();
        openGlossary(t.getAttribute('data-glossary-img'), t.getAttribute('data-glossary-alt') || '');
      });
      document.addEventListener('keydown', function (ev) {
        if (ev.key === 'Escape') { closeGlossary(); return; }
        var t = ev.target && ev.target.closest ? ev.target.closest('.glossary-term') : null;
        if (!t || !t.getAttribute('data-glossary-img')) return;
        if (ev.key === 'Enter' || ev.key === ' ') {
          ev.preventDefault();
          openGlossary(t.getAttribute('data-glossary-img'), t.getAttribute('data-glossary-alt') || '');
        }
      });
      if (lb) lb.addEventListener('click', function (ev) { if (ev.target === lb) closeGlossary(); });
    })();
  </script>
</body>
</html>
""";

    public static string GetHtml(IConfiguration cfg)
    {
        var client = cfg.GetSection("Client");
        string mac = client["DownloadDmgRelativePath"] ?? "/downloads/Hope.dmg";
        string win = client["DownloadWindowsRelativePath"] ?? "/downloads/Hope-Windows.zip";
        return HtmlTemplate
            .Replace("__HDL_MAC__", mac, StringComparison.Ordinal)
            .Replace("__HDL_WIN__", win, StringComparison.Ordinal);
    }
}
