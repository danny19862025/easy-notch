(() => {
  // --------------------------------------------------------------------------
  // 1. Top Scroll Progress Beam
  // --------------------------------------------------------------------------
  const progressBar = document.getElementById("scroll-progress");
  function updateProgress() {
    if (!progressBar) return;
    const h = document.documentElement.scrollHeight - window.innerHeight;
    const pct = h > 0 ? Math.min(100, Math.max(0, (window.scrollY / h) * 100)) : 0;
    progressBar.style.width = `${pct}%`;
  }
  window.addEventListener("scroll", updateProgress, { passive: true });
  updateProgress();

  // --------------------------------------------------------------------------
  // 2. Cinematic Reveal Observer (Masked Lines + Blur-Up Cards)
  // --------------------------------------------------------------------------
  const revealTargets = document.querySelectorAll(".reveal, .reveal-line");
  if ("IntersectionObserver" in window && revealTargets.length) {
    const io = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            entry.target.classList.add("is-visible");
            io.unobserve(entry.target);
          }
        });
      },
      { threshold: 0.12, rootMargin: "0px 0px -28px 0px" }
    );
    revealTargets.forEach((el) => io.observe(el));
  } else {
    revealTargets.forEach((el) => el.classList.add("is-visible"));
  }

  // Trigger hero headline reveal immediately on load
  requestAnimationFrame(() => {
    document.querySelectorAll(".hero-reveal .reveal-line, .hero-reveal .reveal").forEach((el) => {
      el.classList.add("is-visible");
    });
  });

  // --------------------------------------------------------------------------
  // 3. Cursor-Tracking Specular Spotlight on Bento Cards
  // --------------------------------------------------------------------------
  document.querySelectorAll(".spotlight-card").forEach((card) => {
    card.addEventListener("pointermove", (e) => {
      const rect = card.getBoundingClientRect();
      card.style.setProperty("--mx", `${e.clientX - rect.left}px`);
      card.style.setProperty("--my", `${e.clientY - rect.top}px`);
    });
  });

  // --------------------------------------------------------------------------
  // 4. Mobile Navigation
  // --------------------------------------------------------------------------
  const mobileBtn = document.getElementById("mobile-menu-btn");
  const navLinks = document.getElementById("nav-links");
  if (mobileBtn && navLinks) {
    mobileBtn.addEventListener("click", () => {
      const open = navLinks.classList.toggle("open");
      mobileBtn.setAttribute("aria-expanded", String(open));
    });
    navLinks.querySelectorAll("a").forEach((a) => {
      a.addEventListener("click", () => {
        navLinks.classList.remove("open");
        mobileBtn.setAttribute("aria-expanded", "false");
      });
    });
  }

  // Hero top pill jumps smoothly to the interactive reveal showcase
  const heroMiniNotch = document.getElementById("hero-mini-notch");
  if (heroMiniNotch) {
    heroMiniNotch.addEventListener("click", () => {
      document.getElementById("reveal-story")?.scrollIntoView({ behavior: "smooth" });
    });
  }

  // --------------------------------------------------------------------------
  // 5. Scroll-Driven Sticky Notch Reveal Engine
  // --------------------------------------------------------------------------
  const monitorStage = document.getElementById("monitor-stage");
  const notch = document.getElementById("live-notch");
  const handleBtn = document.getElementById("notch-handle-btn");
  const pageLabel = document.getElementById("notch-page-label");
  const dots = document.querySelectorAll(".notch-dot");
  const pages = document.querySelectorAll(".notch-page");
  const dockPills = document.querySelectorAll(".dock-pill[data-dock]");
  const chapters = document.querySelectorAll(".chapter-card");
  const autoBarFill = document.getElementById("notch-auto-fill");
  const autoSlideBtn = document.getElementById("auto-slide-btn");

  const pageOrder = ["clock", "weather", "media", "focus", "notes", "mixer"];
  const pageMeta = {
    pill: { label: "Closed Pill", rgb: "125, 211, 252" },
    clock: { label: "Clock", rgb: "125, 211, 252" },
    weather: { label: "Weather", rgb: "56, 189, 248" },
    media: { label: "Now Playing", rgb: "129, 140, 248" },
    focus: { label: "Focus Timer", rgb: "253, 186, 116" },
    notes: { label: "Notes", rgb: "134, 239, 172" },
    mixer: { label: "Volume Mixer", rgb: "134, 239, 172" }
  };

  let activeIndex = 0;
  let autoSlideEnabled = !window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  let isHoveringNotch = false;
  let autoSlideTimer = null;
  const SLIDE_MS = 3600;

  function restartAutoProgressBar() {
    if (!autoBarFill) return;
    autoBarFill.classList.remove("running");
    void autoBarFill.offsetWidth; // trigger reflow for smooth restart
    if (autoSlideEnabled && !isHoveringNotch && notch?.classList.contains("expanded")) {
      autoBarFill.classList.add("running");
    }
  }

  function stopAutoTimer() {
    if (autoSlideTimer) {
      clearTimeout(autoSlideTimer);
      autoSlideTimer = null;
    }
    if (autoBarFill) autoBarFill.classList.remove("running");
  }

  function scheduleNextSlide() {
    stopAutoTimer();
    if (!autoSlideEnabled || isHoveringNotch || !notch?.classList.contains("expanded")) {
      return;
    }
    restartAutoProgressBar();
    autoSlideTimer = setTimeout(() => {
      const nextIdx = (activeIndex + 1) % pageOrder.length;
      showNotchState(pageOrder[nextIdx]);
    }, SLIDE_MS);
  }

  function setStageRGB(key) {
    const meta = pageMeta[key] || pageMeta.clock;
    if (monitorStage) monitorStage.style.setProperty("--stage-rgb", meta.rgb);
  }

  function highlightChapter(stateKey) {
    chapters.forEach((ch) => {
      ch.classList.toggle("is-active", ch.dataset.chapter === stateKey);
    });
    dockPills.forEach((dp) => {
      dp.classList.toggle("active", dp.dataset.dock === stateKey);
    });
  }

  function showNotchState(stateKey) {
    if (stateKey === "pill") {
      stopAutoTimer();
      if (notch) {
        notch.classList.remove("expanded");
        notch.setAttribute("aria-expanded", "false");
      }
      setStageRGB("pill");
      highlightChapter("pill");
      return;
    }

    const idx = pageOrder.indexOf(stateKey);
    if (idx === -1) return;
    activeIndex = idx;

    if (notch) {
      notch.classList.add("expanded");
      notch.setAttribute("aria-expanded", "true");
    }

    pages.forEach((p) => {
      const pIdx = pageOrder.indexOf(p.dataset.page);
      const isAct = pIdx === idx;
      p.classList.toggle("active", isAct);
      p.classList.toggle("is-prev", pIdx < idx);
    });
    dots.forEach((d) => {
      const act = d.dataset.dot === stateKey;
      d.classList.toggle("active", act);
      d.setAttribute("aria-selected", String(act));
    });
    if (pageLabel) pageLabel.textContent = pageMeta[stateKey]?.label || stateKey;
    setStageRGB(stateKey);
    highlightChapter(stateKey);
    scheduleNextSlide();
  }

  if (autoSlideBtn) {
    autoSlideBtn.addEventListener("click", () => {
      autoSlideEnabled = !autoSlideEnabled;
      autoSlideBtn.textContent = autoSlideEnabled ? "Auto ●" : "Auto ○";
      autoSlideBtn.classList.toggle("active", autoSlideEnabled);
      if (autoSlideEnabled) {
        if (!notch?.classList.contains("expanded")) {
          showNotchState(pageOrder[activeIndex]);
        } else {
          scheduleNextSlide();
        }
      } else {
        stopAutoTimer();
      }
    });
  }

  if (notch) {
    notch.addEventListener("pointerenter", () => {
      isHoveringNotch = true;
      stopAutoTimer();
    });
    notch.addEventListener("pointerleave", () => {
      isHoveringNotch = false;
      scheduleNextSlide();
    });
  }

  // Kick off the reveal: start expanded on Clock and auto-slide smoothly
  setTimeout(() => {
    showNotchState("clock");
  }, 650);

  // Click interactions on notch, dots, dock, and chapters
  if (notch) {
    notch.addEventListener("click", () => {
      if (!notch.classList.contains("expanded")) {
        showNotchState(pageOrder[activeIndex]);
      }
    });

    let wheelLock = false;
    notch.addEventListener(
      "wheel",
      (e) => {
        if (!notch.classList.contains("expanded")) return;
        e.preventDefault();
        if (wheelLock) return;
        wheelLock = true;
        const dir = e.deltaY > 0 ? 1 : -1;
        const nextIdx = (activeIndex + dir + pageOrder.length) % pageOrder.length;
        showNotchState(pageOrder[nextIdx]);
        setTimeout(() => {
          wheelLock = false;
        }, 260);
      },
      { passive: false }
    );
  }

  if (handleBtn) {
    handleBtn.addEventListener("click", (e) => {
      e.stopPropagation();
      showNotchState("pill");
    });
  }

  dots.forEach((dot) => {
    dot.addEventListener("click", (e) => {
      e.stopPropagation();
      showNotchState(dot.dataset.dot);
    });
  });

  dockPills.forEach((pill) => {
    pill.addEventListener("click", () => showNotchState(pill.dataset.dock));
  });

  chapters.forEach((ch) => {
    ch.addEventListener("click", () => showNotchState(ch.dataset.chapter));
  });

  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && notch?.classList.contains("expanded")) {
      showNotchState("pill");
    }
  });

  // Scroll-Driven Chapter Activation (desktop sticky storytelling)
  if ("IntersectionObserver" in window && chapters.length) {
    const chapterObserver = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            const key = entry.target.dataset.chapter;
            if (key && !(key === "pill" && autoSlideEnabled)) {
              showNotchState(key);
            }
          }
        });
      },
      {
        rootMargin: "-38% 0px -42% 0px",
        threshold: 0.05
      }
    );
    chapters.forEach((ch) => chapterObserver.observe(ch));
  }

  // --------------------------------------------------------------------------
  // 6. Live Clocks (Hero Pill + Notch Pill + World Places)
  // --------------------------------------------------------------------------
  const heroTimeEl = document.getElementById("hero-pill-time");
  const closedTimeEl = document.getElementById("notch-closed-time");
  const bigTimeEl = document.getElementById("n-big-time");
  const dateEl = document.getElementById("n-date");
  const cityLabelEl = document.getElementById("n-city-label");
  const cityButtons = document.querySelectorAll(".n-city-btn");

  let selectedTz = null;
  let selectedCityName = "Local Time";

  function formatTime(tz) {
    const opts = { hour: "2-digit", minute: "2-digit", hour12: false };
    if (tz) opts.timeZone = tz;
    return new Intl.DateTimeFormat(undefined, opts).format(new Date());
  }

  function formatDate(tz) {
    const opts = { weekday: "long", month: "short", day: "numeric" };
    if (tz) opts.timeZone = tz;
    return new Intl.DateTimeFormat(undefined, opts).format(new Date());
  }

  function updateClocks() {
    const localNow = formatTime(null);
    if (heroTimeEl) heroTimeEl.textContent = localNow;
    if (closedTimeEl) closedTimeEl.textContent = localNow;
    if (bigTimeEl) bigTimeEl.textContent = formatTime(selectedTz);
    if (dateEl) dateEl.textContent = formatDate(selectedTz);
    if (cityLabelEl) cityLabelEl.textContent = selectedCityName;

    cityButtons.forEach((btn) => {
      const span = btn.querySelector(".city-time");
      if (span) span.textContent = formatTime(btn.dataset.tz || null);
    });
  }

  cityButtons.forEach((btn) => {
    btn.addEventListener("click", (e) => {
      e.stopPropagation();
      cityButtons.forEach((b) => b.classList.remove("active"));
      btn.classList.add("active");
      selectedTz = btn.dataset.tz || null;
      selectedCityName = btn.dataset.city || "Local Time";
      updateClocks();
    });
  });

  updateClocks();
  setInterval(updateClocks, 15000);

  // --------------------------------------------------------------------------
  // 7. Weather °C / °F Toggle
  // --------------------------------------------------------------------------
  const unitBtn = document.getElementById("n-unit-btn");
  const tempMain = document.getElementById("n-weather-temp");
  const closedTemp = document.getElementById("notch-closed-temp");
  const heroTemp = document.getElementById("hero-pill-temp");
  const hourVals = document.querySelectorAll("[data-temp-c]");
  let isF = false;

  if (unitBtn) {
    unitBtn.addEventListener("click", (e) => {
      e.stopPropagation();
      isF = !isF;
      const conv = (c) => (isF ? Math.round((c * 9) / 5 + 32) : c);
      if (tempMain) tempMain.textContent = `${conv(21)}°`;
      if (closedTemp) closedTemp.textContent = `${conv(21)}°`;
      if (heroTemp) heroTemp.textContent = `${conv(21)}°`;
      unitBtn.textContent = isF ? "°F" : "°C";
      hourVals.forEach((el) => {
        el.textContent = `${conv(Number(el.dataset.tempC))}°`;
      });
    });
  }

  // --------------------------------------------------------------------------
  // 8. Media Play/Pause & Equalizer Sync
  // --------------------------------------------------------------------------
  const playBtn = document.getElementById("n-play-btn");
  const eqBarsAll = document.querySelectorAll(".eq-bars");
  const seekFill = document.getElementById("n-seek-fill");
  let isPlaying = true;
  let seekPct = 46;

  if (playBtn) {
    playBtn.addEventListener("click", (e) => {
      e.stopPropagation();
      isPlaying = !isPlaying;
      playBtn.innerHTML = isPlaying
        ? '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><rect x="6" y="4" width="4" height="16"/><rect x="14" y="4" width="4" height="16"/></svg>'
        : '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><polygon points="6,4 20,12 6,20"/></svg>';
      eqBarsAll.forEach((eq) => eq.classList.toggle("paused", !isPlaying));
    });
  }

  setInterval(() => {
    if (!isPlaying || !seekFill) return;
    seekPct = seekPct >= 98 ? 10 : seekPct + 0.5;
    seekFill.style.width = `${seekPct}%`;
  }, 1000);

  // --------------------------------------------------------------------------
  // 9. Focus Timer & Ambient Noise Selector
  // --------------------------------------------------------------------------
  const focusTimerEl = document.getElementById("n-focus-timer");
  const focusToggleBtn = document.getElementById("n-focus-toggle");
  const noisePills = document.querySelectorAll("[data-noise]");
  let focusSeconds = 25 * 60;
  let focusRunning = false;

  if (focusToggleBtn) {
    focusToggleBtn.addEventListener("click", (e) => {
      e.stopPropagation();
      focusRunning = !focusRunning;
      focusToggleBtn.textContent = focusRunning ? "Pause" : "Start Focus";
    });
  }

  setInterval(() => {
    if (!focusRunning || !focusTimerEl) return;
    focusSeconds = focusSeconds > 0 ? focusSeconds - 1 : 25 * 60;
    const m = String(Math.floor(focusSeconds / 60)).padStart(2, "0");
    const s = String(focusSeconds % 60).padStart(2, "0");
    focusTimerEl.textContent = `${m}:${s}`;
  }, 1000);

  noisePills.forEach((pill) => {
    pill.addEventListener("click", (e) => {
      e.stopPropagation();
      noisePills.forEach((p) => p.classList.remove("active"));
      pill.classList.add("active");
    });
  });

  // --------------------------------------------------------------------------
  // 10. Mixer Sliders & Install Tabs + Copy
  // --------------------------------------------------------------------------
  document.querySelectorAll(".n-mixer-row input[type='range']").forEach((slider) => {
    slider.addEventListener("input", (e) => {
      e.stopPropagation();
      const valEl = slider.parentElement?.querySelector(".n-mixer-val");
      if (valEl) valEl.textContent = `${slider.value}%`;
    });
  });

  const installTabs = document.querySelectorAll("[data-install-tab]");
  const installPanels = document.querySelectorAll("[data-install-panel]");

  installTabs.forEach((tab) => {
    tab.addEventListener("click", () => {
      const target = tab.dataset.installTab;
      installTabs.forEach((t) => {
        t.classList.remove("active");
        t.setAttribute("aria-selected", "false");
      });
      installPanels.forEach((p) =>
        p.classList.toggle("active", p.dataset.installPanel === target)
      );
      tab.classList.add("active");
      tab.setAttribute("aria-selected", "true");
    });
  });

  document.querySelectorAll(".copy-btn").forEach((btn) => {
    btn.addEventListener("click", async () => {
      const code = btn.closest(".code-block")?.querySelector("pre")?.innerText;
      if (!code) return;
      try {
        await navigator.clipboard.writeText(code.trim());
        const prev = btn.textContent;
        btn.textContent = "Copied!";
        btn.classList.add("copied");
        setTimeout(() => {
          btn.textContent = prev;
          btn.classList.remove("copied");
        }, 1800);
      } catch {
        btn.textContent = "Copy manually";
      }
    });
  });
})();
