(() => {
  // --------------------------------------------------------------------------
  // Scroll Reveal Choreography (IntersectionObserver)
  // --------------------------------------------------------------------------
  const revealEls = document.querySelectorAll(".reveal");
  if ("IntersectionObserver" in window && revealEls.length) {
    const observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            entry.target.classList.add("is-visible");
            observer.unobserve(entry.target);
          }
        });
      },
      { threshold: 0.12, rootMargin: "0px 0px -36px 0px" }
    );
    revealEls.forEach((el) => observer.observe(el));
  } else {
    revealEls.forEach((el) => el.classList.add("is-visible"));
  }

  // --------------------------------------------------------------------------
  // Mobile Navigation
  // --------------------------------------------------------------------------
  const mobileBtn = document.getElementById("mobile-menu-btn");
  const navLinks = document.getElementById("nav-links");
  if (mobileBtn && navLinks) {
    mobileBtn.addEventListener("click", () => {
      const open = navLinks.classList.toggle("open");
      mobileBtn.setAttribute("aria-expanded", String(open));
    });
    navLinks.querySelectorAll("a").forEach((link) => {
      link.addEventListener("click", () => {
        navLinks.classList.remove("open");
        mobileBtn.setAttribute("aria-expanded", "false");
      });
    });
  }

  // --------------------------------------------------------------------------
  // Live Interactive Easy Notch Demo
  // --------------------------------------------------------------------------
  const notch = document.getElementById("live-notch");
  const heroGlow = document.getElementById("hero-glow");
  const handleBtn = document.getElementById("notch-handle-btn");
  const pageLabel = document.getElementById("notch-page-label");
  const stageHint = document.getElementById("stage-hint");
  const dots = document.querySelectorAll(".notch-dot");
  const pages = document.querySelectorAll(".notch-page");
  const stagePills = document.querySelectorAll("[data-demo-page]");

  const pageOrder = ["clock", "weather", "media", "focus", "notes", "mixer"];
  const pageNames = {
    clock: "Clock",
    weather: "Weather",
    media: "Now Playing",
    focus: "Focus Timer",
    notes: "Notes",
    mixer: "Volume Mixer"
  };

  let activeIndex = 0;

  function setGlowMode(pageKey) {
    if (!heroGlow) return;
    heroGlow.classList.remove("mode-focus", "mode-rest");
    if (pageKey === "focus") heroGlow.classList.add("mode-focus");
    if (pageKey === "mixer" || pageKey === "notes") heroGlow.classList.add("mode-rest");
  }

  function showPage(key, expand = true) {
    const idx = pageOrder.indexOf(key);
    if (idx === -1) return;
    activeIndex = idx;

    if (expand && notch) {
      notch.classList.add("expanded");
      notch.setAttribute("aria-expanded", "true");
      if (stageHint) {
        stageHint.textContent =
          "Scroll over the notch or click the dots on the right to switch pages. Click the top handle or press Esc to close.";
      }
    }

    pages.forEach((p) => p.classList.toggle("active", p.dataset.page === key));
    dots.forEach((d) => {
      const isAct = d.dataset.dot === key;
      d.classList.toggle("active", isAct);
      d.setAttribute("aria-selected", String(isAct));
    });
    stagePills.forEach((b) =>
      b.classList.toggle("active", b.dataset.demoPage === key)
    );

    if (pageLabel) pageLabel.textContent = pageNames[key] || key;
    setGlowMode(key);
  }

  function collapseNotch() {
    if (!notch) return;
    notch.classList.remove("expanded");
    notch.setAttribute("aria-expanded", "false");
    if (heroGlow) heroGlow.classList.remove("mode-focus", "mode-rest");
    if (stageHint) {
      stageHint.textContent =
        "Click the notch above to reveal it — or jump straight to a page below.";
    }
  }

  if (notch) {
    notch.addEventListener("click", (e) => {
      if (!notch.classList.contains("expanded")) {
        showPage(pageOrder[activeIndex], true);
      }
    });

    // Wheel scroll inside expanded notch switches pages just like the real app!
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
        showPage(pageOrder[nextIdx], true);
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
      collapseNotch();
    });
  }

  dots.forEach((dot) => {
    dot.addEventListener("click", (e) => {
      e.stopPropagation();
      showPage(dot.dataset.dot, true);
    });
  });

  stagePills.forEach((pill) => {
    pill.addEventListener("click", () => {
      const key = pill.dataset.demoPage;
      if (key === "close") {
        collapseNotch();
      } else {
        showPage(key, true);
      }
    });
  });

  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && notch && notch.classList.contains("expanded")) {
      collapseNotch();
    }
  });

  // Auto-reveal once gently after landing so the user immediately sees the morph
  const prefersReduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  if (!prefersReduced) {
    setTimeout(() => {
      if (notch && !notch.classList.contains("expanded")) {
        showPage("clock", true);
      }
    }, 950);
  } else if (notch) {
    showPage("clock", true);
  }

  // --------------------------------------------------------------------------
  // Live Clock & World Places
  // --------------------------------------------------------------------------
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
    if (closedTimeEl) closedTimeEl.textContent = formatTime(null);
    if (bigTimeEl) bigTimeEl.textContent = formatTime(selectedTz);
    if (dateEl) dateEl.textContent = formatDate(selectedTz);
    if (cityLabelEl) cityLabelEl.textContent = selectedCityName;

    cityButtons.forEach((btn) => {
      const tz = btn.dataset.tz;
      const span = btn.querySelector(".city-time");
      if (span) span.textContent = formatTime(tz || null);
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
  // Interactive Weather °C / °F Toggle
  // --------------------------------------------------------------------------
  const unitBtn = document.getElementById("n-unit-btn");
  const tempMain = document.getElementById("n-weather-temp");
  const closedTemp = document.getElementById("notch-closed-temp");
  const hourVals = document.querySelectorAll("[data-temp-c]");
  let isFahrenheit = false;

  function renderTemps() {
    const conv = (c) => (isFahrenheit ? Math.round((c * 9) / 5 + 32) : c);
    const suffix = isFahrenheit ? "°F" : "°C";
    if (tempMain) tempMain.textContent = `${conv(21)}°`;
    if (closedTemp) closedTemp.textContent = `${conv(21)}°`;
    if (unitBtn) unitBtn.textContent = suffix;
    hourVals.forEach((el) => {
      const c = Number(el.dataset.tempC);
      el.textContent = `${conv(c)}°`;
    });
  }

  if (unitBtn) {
    unitBtn.addEventListener("click", (e) => {
      e.stopPropagation();
      isFahrenheit = !isFahrenheit;
      renderTemps();
    });
  }

  // --------------------------------------------------------------------------
  // Interactive Media Play/Pause & Equalizer Sync
  // --------------------------------------------------------------------------
  const playBtn = document.getElementById("n-play-btn");
  const eqBars = document.getElementById("notch-eq");
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
      if (eqBars) eqBars.classList.toggle("paused", !isPlaying);
    });
  }

  setInterval(() => {
    if (!isPlaying || !seekFill) return;
    seekPct = seekPct >= 98 ? 10 : seekPct + 0.5;
    seekFill.style.width = `${seekPct}%`;
  }, 1000);

  // --------------------------------------------------------------------------
  // Interactive Focus Countdown & Noise Pills
  // --------------------------------------------------------------------------
  const focusTimerEl = document.getElementById("n-focus-timer");
  const focusToggleBtn = document.getElementById("n-focus-toggle");
  const noisePills = document.querySelectorAll("[data-noise]");
  let focusSeconds = 25 * 60;
  let focusRunning = false;

  function renderFocus() {
    if (!focusTimerEl) return;
    const m = String(Math.floor(focusSeconds / 60)).padStart(2, "0");
    const s = String(focusSeconds % 60).padStart(2, "0");
    focusTimerEl.textContent = `${m}:${s}`;
  }

  if (focusToggleBtn) {
    focusToggleBtn.addEventListener("click", (e) => {
      e.stopPropagation();
      focusRunning = !focusRunning;
      focusToggleBtn.textContent = focusRunning ? "Pause" : "Start Focus";
    });
  }

  setInterval(() => {
    if (!focusRunning) return;
    focusSeconds = focusSeconds > 0 ? focusSeconds - 1 : 25 * 60;
    renderFocus();
  }, 1000);

  noisePills.forEach((pill) => {
    pill.addEventListener("click", (e) => {
      e.stopPropagation();
      noisePills.forEach((p) => p.classList.remove("active"));
      pill.classList.add("active");
    });
  });

  // --------------------------------------------------------------------------
  // Interactive Mixer Sliders
  // --------------------------------------------------------------------------
  document.querySelectorAll(".n-mixer-row input[type='range']").forEach((slider) => {
    slider.addEventListener("input", (e) => {
      e.stopPropagation();
      const valEl = slider.parentElement?.querySelector(".n-mixer-val");
      if (valEl) valEl.textContent = `${slider.value}%`;
    });
  });

  // --------------------------------------------------------------------------
  // Install Tabs & Copy Buttons
  // --------------------------------------------------------------------------
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
