const motionAllowed = !window.matchMedia("(prefers-reduced-motion: reduce)").matches;

const lightbox = document.querySelector(".lightbox");
const lightboxImage = lightbox?.querySelector("img");
const lightboxCaption = lightbox?.querySelector("figcaption");
const lightboxClose = lightbox?.querySelector(".lightbox-close");

function addRipple(target, clientX, clientY) {
  const rect = target.getBoundingClientRect();
  const ripple = document.createElement("span");
  ripple.className = "click-ripple";
  ripple.style.left = `${clientX - rect.left}px`;
  ripple.style.top = `${clientY - rect.top}px`;
  target.appendChild(ripple);
  window.setTimeout(() => ripple.remove(), 700);
}

document.querySelectorAll(".zoom-trigger").forEach((trigger) => {
  trigger.addEventListener("click", () => {
    if (!lightbox || !lightboxImage || !lightboxCaption) {
      return;
    }

    lightboxImage.src = trigger.dataset.full ?? "";
    lightboxImage.alt = trigger.querySelector("img")?.alt ?? "Application screenshot";
    lightboxCaption.textContent = trigger.dataset.title ?? "Application screenshot";
    lightbox.showModal();
  });
});

lightboxClose?.addEventListener("click", () => lightbox?.close());

lightbox?.addEventListener("click", (event) => {
  if (event.target === lightbox) {
    lightbox.close();
  }
});

document.addEventListener("keydown", (event) => {
  if (event.key === "Escape" && lightbox?.open) {
    lightbox.close();
  }
});

const kineticSelector = [
  "a",
  "button",
  ".interactive-surface",
  ".value-card",
  ".feature-card",
  ".evidence-card",
  ".report-list > div",
  ".trust-strip span",
  ".metric-row div",
  ".ticker-track span",
  ".screen-card"
].join(", ");

const largeSelector = [
  ".hero-panel",
  ".author-card",
  ".download-card",
  ".value-card",
  ".feature-card",
  ".evidence-card",
  ".screen-card",
  ".screen-frame"
].join(", ");

document.querySelectorAll(kineticSelector).forEach((target) => {
  target.classList.add("kinetic-ready");

  target.addEventListener("pointerdown", (event) => {
    target.classList.add("is-pressed");
    addRipple(target, event.clientX, event.clientY);
  });

  target.addEventListener("pointerup", () => {
    target.classList.remove("is-pressed");
  });

  target.addEventListener("pointercancel", () => {
    target.classList.remove("is-pressed");
  });

  target.addEventListener("pointerleave", () => {
    target.classList.remove("is-pressed");
    if (!motionAllowed) return;
    target.style.setProperty("--mx", "50%");
    target.style.setProperty("--my", "50%");
    target.style.setProperty("--tx", "0px");
    target.style.setProperty("--ty", "0px");
    target.style.setProperty("--rx", "0deg");
    target.style.setProperty("--ry", "0deg");
  });

  if (!motionAllowed) {
    return;
  }

  target.addEventListener("pointermove", (event) => {
    const rect = target.getBoundingClientRect();
    const localX = event.clientX - rect.left;
    const localY = event.clientY - rect.top;
    const px = localX / rect.width - 0.5;
    const py = localY / rect.height - 0.5;
    const isLarge = target.matches(largeSelector);
    const magnet = isLarge ? 2.2 : 7.5;
    const tiltX = isLarge ? -py * 4.2 : 0;
    const tiltY = isLarge ? px * 5.2 : 0;

    target.style.setProperty("--mx", `${localX}px`);
    target.style.setProperty("--my", `${localY}px`);
    target.style.setProperty("--tx", `${px * magnet}px`);
    target.style.setProperty("--ty", `${py * magnet}px`);
    target.style.setProperty("--rx", `${tiltX}deg`);
    target.style.setProperty("--ry", `${tiltY}deg`);
  });
});
