const lightbox = document.querySelector(".lightbox");
const lightboxImage = lightbox?.querySelector("img");
const lightboxCaption = lightbox?.querySelector("figcaption");
const lightboxClose = lightbox?.querySelector(".lightbox-close");

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

document.querySelectorAll("a, button, .interactive-surface").forEach((target) => {
  target.addEventListener("pointerdown", (event) => {
    const rect = target.getBoundingClientRect();
    const ripple = document.createElement("span");
    ripple.className = "click-ripple";
    ripple.style.left = `${event.clientX - rect.left}px`;
    ripple.style.top = `${event.clientY - rect.top}px`;
    target.appendChild(ripple);
    window.setTimeout(() => ripple.remove(), 620);
  });

  target.addEventListener("keydown", (event) => {
    if (event.key !== "Enter" && event.key !== " ") {
      return;
    }

    if (target.matches("a, button")) {
      return;
    }

    event.preventDefault();
    const ripple = document.createElement("span");
    ripple.className = "click-ripple";
    ripple.style.left = "50%";
    ripple.style.top = "50%";
    target.appendChild(ripple);
    window.setTimeout(() => ripple.remove(), 620);
  });
});
