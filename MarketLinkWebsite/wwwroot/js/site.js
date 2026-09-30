document.addEventListener('DOMContentLoaded', () => {
    initializeHeader();
    initializeTooltips();
    initializeThemeToggle();
    initializePasswordToggles();
    initializeConfirmations();
    initializeFavorites();
    initializeCartActions();
    initializeQuantityControls();
    initializeRevealAnimations();
    initializeAssistant();
    initializeImageFallbacks();
    initializeImagePreviews();
    initializeFormGuards();
    initializeGeoLocation();
    initializeMarketCoordinateFill();
});

function initializeMarketCoordinateFill() {
    const lat = document.getElementById('Latitude');
    const lon = document.getElementById('Longitude');
    if (!lat || !lon) return;

    const boxes = [...document.querySelectorAll('input[name="MarketIds"][data-market-lat]')];
    if (boxes.length === 0) return;

    lat.addEventListener('input', () => { lat.dataset.marketTouched = 'true'; });

    const apply = () => {
        if (lat.dataset.marketTouched === 'true') return;

        const chosen = boxes.filter(box => box.checked && parseFloat(box.dataset.marketLat) !== 0);
        if (chosen.length === 0) return;

        const currentIsBlank = lat.value.trim() === '' || parseFloat(lat.value) === 0;
        if (!currentIsBlank) return;

        lat.value = parseFloat(chosen[0].dataset.marketLat).toFixed(6);
        lon.value = parseFloat(chosen[0].dataset.marketLon).toFixed(6);
    };

    boxes.forEach(box => box.addEventListener('change', apply));
}

function initializeGeoLocation() {
    const key = 'ml:geo-asked';
    if (!navigator.geolocation || !navigator.permissions) return;

    if (document.cookie.indexOf('ml-geo=') !== -1) {
        markDistanceReady();
        return;
    }

    navigator.permissions.query({ name: 'geolocation' }).then(result => {
        if (result.state === 'granted') {
            captureLocation();
            return;
        }
        if (result.state === 'prompt' && !sessionStorage.getItem(key)) {
            sessionStorage.setItem(key, '1');
            captureLocation();
        }
    }).catch(() => {});
}

function captureLocation() {
    navigator.geolocation.getCurrentPosition(position => {
        const body = new URLSearchParams({
            latitude: position.coords.latitude.toFixed(5),
            longitude: position.coords.longitude.toFixed(5)
        });
        fetch('/Geo/Save', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: body.toString(),
            credentials: 'same-origin'
        }).then(response => {
            if (response.ok) markDistanceReady();
        }).catch(() => {});
    }, () => {}, { timeout: 8000, maximumAge: 600000, enableHighAccuracy: false });
}

function markDistanceReady() {
    document.querySelectorAll('[data-requires-distance]').forEach(node => {
        node.hidden = false;
        node.classList.remove('is-hidden');
    });
    document.querySelectorAll('[data-ask-location]').forEach(node => node.remove());
}

function initializeImageFallbacks() {
    const mark = image => {
        if (image.dataset.fallbackApplied === 'true') return;
        image.dataset.fallbackApplied = 'true';
        image.addEventListener('error', () => {
            const holder = image.parentElement;
            if (!holder || holder.querySelector('.image-fallback')) return;
            image.style.display = 'none';
            const badge = document.createElement('span');
            badge.className = 'image-fallback';
            badge.setAttribute('aria-hidden', 'true');
            badge.innerHTML = '<i class="bi bi-image"></i>';
            holder.appendChild(badge);
        });
    };

    document.querySelectorAll('img[src]').forEach(mark);
}

function initializeImagePreviews() {
    document.querySelectorAll('[data-image-preview-for]').forEach(preview => {
        const field = document.getElementById(preview.dataset.imagePreviewFor);
        if (!field) return;

        const image = preview.querySelector('img');
        const copy = preview.querySelector('.image-preview-copy');
        const title = copy?.querySelector('strong');
        const detail = copy?.querySelector('span:last-child');

        const refresh = () => {
            const value = (field.value || '').trim();

            if (value.length === 0) {
                preview.classList.add('is-hidden');
                return;
            }

            preview.classList.remove('is-hidden');
            copy.classList.remove('is-error', 'is-ok');

            if (!/^https?:\/\//i.test(value)) {
                copy.classList.add('is-error');
                title.textContent = 'That does not look like a web address';
                detail.textContent = 'Start the link with https:// so the picture can load.';
                return;
            }

            copy.classList.add('is-ok');
            title.textContent = 'Checking the picture';
            detail.textContent = value;
            image.style.display = '';
            image.src = value;
        };

        field.addEventListener('input', refresh);
        field.addEventListener('change', refresh);

        image.addEventListener('load', () => {
            copy.classList.remove('is-error');
            copy.classList.add('is-ok');
            title.textContent = 'Picture looks good';
        });

        image.addEventListener('error', () => {
            copy.classList.remove('is-ok');
            copy.classList.add('is-error');
            title.textContent = 'That picture could not be loaded';
            detail.textContent = 'Check the link is public and ends with an image file.';
        });

        refresh();
    });
}

function initializeFormGuards() {
    document.querySelectorAll('form[data-guard]').forEach(form => {
        const clearAlert = () => {
            const box = form.querySelector('[data-guard-alert]');
            if (box) box.remove();
        };

        const showAlert = invalid => {
            let box = form.querySelector('[data-guard-alert]');
            if (!box) {
                box = document.createElement('div');
                box.className = 'form-hint-alert';
                box.setAttribute('role', 'alert');
                box.dataset.guardAlert = 'true';
                box.innerHTML = '<i class="bi bi-exclamation-octagon-fill"></i><span></span>';
                form.prepend(box);
            }

            const label = invalid && invalid.id ? form.querySelector('label[for="' + invalid.id + '"]') : null;
            const name = label && label.textContent.trim()
                ? label.textContent.trim().replace(/\*$/, '')
                : invalid && invalid.name
                    ? invalid.name
                    : 'A required field';

            box.querySelector('span').textContent = name + ' needs your attention. Please complete the highlighted fields and try again.';
            return box;
        };

        form.addEventListener('submit', event => {
            if (form.checkValidity()) {
                clearAlert();
                return;
            }

            const invalid = form.querySelector(':invalid');
            if (!invalid) return;

            event.preventDefault();
            const box = showAlert(invalid);
            box.scrollIntoView({ behavior: 'smooth', block: 'center' });
            invalid.focus({ preventScroll: true });
        });

        form.querySelectorAll('input, select, textarea').forEach(field => {
            const recheck = () => {
                if (form.checkValidity()) clearAlert();
            };
            field.addEventListener('input', recheck);
            field.addEventListener('change', recheck);
        });
    });
}

function initializeThemeToggle() {
    const toggles = document.querySelectorAll('[data-theme-toggle]');
    if (!toggles.length) return;

    const paint = () => {
        const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
        toggles.forEach(button => {
            button.setAttribute('aria-pressed', String(isDark));
            button.setAttribute('aria-label', isDark ? 'Switch to light theme' : 'Switch to dark theme');
            const icon = button.querySelector('[data-theme-icon]');
            if (icon) {
                icon.className = `bi ${isDark ? 'bi-sun-fill' : 'bi-moon-stars-fill'}`;
            }
        });
    };

    paint();

    toggles.forEach(button => {
        button.addEventListener('click', () => {
            const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
            const next = isDark ? 'light' : 'dark';
            document.documentElement.setAttribute('data-theme', next);
            try {
                localStorage.setItem('marketlink-theme', next);
            } catch (error) {
                void error;
            }
            paint();
        });
    });
}

function initializeConfirmations() {
    document.querySelectorAll('form[data-confirm]').forEach(form => {
        form.addEventListener('submit', event => {
            const message = form.getAttribute('data-confirm');
            if (message && !window.confirm(message)) {
                event.preventDefault();
            }
        });
    });
}

function initializeHeader() {
    const header = document.getElementById('mainNav');
    if (!header) return;

    const updateHeader = () => header.classList.toggle('is-scrolled', window.scrollY > 12);
    updateHeader();
    window.addEventListener('scroll', updateHeader, { passive: true });
}

function initializeTooltips() {
    if (window.bootstrap) {
        document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(element => {
            new bootstrap.Tooltip(element);
        });
    }
}

function initializePasswordToggles() {
    document.querySelectorAll('[data-password-toggle]').forEach(toggle => {
        const input = document.querySelector(toggle.getAttribute('data-password-toggle') || '[data-password-input]');
        if (!input) return;

        toggle.addEventListener('click', () => {
            const reveal = input.type === 'password';
            input.type = reveal ? 'text' : 'password';
            toggle.setAttribute('aria-pressed', String(reveal));
            toggle.setAttribute('aria-label', reveal ? 'Hide password' : 'Show password');

            const icon = toggle.querySelector('[data-password-icon]');
            if (icon) {
                icon.className = `bi ${reveal ? 'bi-eye-slash' : 'bi-eye'}`;
            }
        });
    });
}

function updateFavouriteBadge(delta) {
    document.querySelectorAll('[data-favourite-count]').forEach(badge => {
        const current = Number(badge.textContent || '0') || 0;
        const next = Math.max(0, current + delta);
        badge.textContent = String(next);
        badge.classList.toggle('cart-count-empty', next === 0);

        const host = badge.closest('[data-favourite-badge-host]');
        if (host) {
            host.setAttribute('aria-label', next > 0 ? `${next} saved items` : 'No saved items yet');
        }
    });
}

function initializeFavorites() {
    document.querySelectorAll('[data-favorite]').forEach(button => {
        button.addEventListener('click', event => {
            const form = button.closest('form');
            if (!form) return;

            event.preventDefault();

            if (button.dataset.busy === 'true') return;
            button.dataset.busy = 'true';

            const wasActive = button.classList.contains('is-active');
            const icon = button.querySelector('i');
            const label = button.getAttribute('aria-label') || '';
            const productName = label.replace(/^(Save|Unsave) /, '').replace(' to favorites', '').replace(' from favorites', '') || 'Item';

            const payload = new FormData(form);

            fetch(form.action, {
                method: 'POST',
                body: payload,
                credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                redirect: 'follow'
            })
                .then(response => {
                    const destination = response.url || '';

                    if (destination.includes('/Account/Login')) {
                        window.location.href = destination;
                        return;
                    }

                    if (!response.ok) {
                        throw new Error('Request failed');
                    }

                    const isActive = button.classList.toggle('is-active');
                    button.setAttribute('aria-pressed', String(isActive));

                    if (icon) {
                        icon.className = `bi ${isActive ? 'bi-heart-fill' : 'bi-heart'}`;
                    }

                    const isFarmer = button.dataset.farmerKind === 'farmer';
                    const isMarket = button.dataset.marketKind === 'market';
                    if (!isFarmer && !isMarket) {
                        const delta = isActive === wasActive ? 0 : (isActive ? 1 : -1);
                        if (delta !== 0) {
                            updateFavouriteBadge(delta);
                        }
                    } else {
                        // The saved list is rendered by the server, so refresh to show it.
                        window.setTimeout(() => window.location.reload(), 450);
                    }

                    showToast(isActive ? `${productName} saved to favorites.` : `${productName} removed from favorites.`);
                })
                .catch(() => {
                    showToast('Could not update favorites. Please try again.');
                })
                .finally(() => {
                    button.dataset.busy = 'false';
                });
        });
    });
}

function initializeCartActions() {
    document.querySelectorAll('[data-add-to-cart]').forEach(button => {
        button.addEventListener('click', () => {
            const productName = button.getAttribute('data-product-name') || 'Item';
            showToast(`${productName} added to your pre-order basket.`);
        });
    });
}

function formatMoney(value) {
    return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function refreshCartTotals() {
    let subtotal = 0;

    document.querySelectorAll('[data-cart-item]').forEach(row => {
        const unitPrice = Number(row.dataset.unitPrice || 0);
        const input = row.querySelector('[data-quantity-control] input');
        const quantity = Math.max(1, Number(input ? input.value : 1) || 1);
        const lineTotal = unitPrice * quantity;

        const lineTarget = row.querySelector('[data-cart-line-total]');
        if (lineTarget) lineTarget.textContent = formatMoney(lineTotal);

        subtotal += lineTotal;
    });

    const subtotalTarget = document.querySelector('[data-cart-subtotal]');
    if (subtotalTarget) subtotalTarget.textContent = formatMoney(subtotal);

    const totalTarget = document.querySelector('[data-cart-total]');
    if (totalTarget) totalTarget.textContent = formatMoney(subtotal);

    const countTargets = document.querySelectorAll('[data-cart-item-count]');
    const count = document.querySelectorAll('[data-cart-item]').length;
    countTargets.forEach(node => { node.textContent = String(count); });
}

function initializeQuantityControls() {
    document.querySelectorAll('[data-quantity-control]').forEach(control => {
        const input = control.querySelector('input');
        const decrease = control.querySelector('[data-quantity-decrease]');
        const increase = control.querySelector('[data-quantity-increase]');
        if (!input) return;

        const form = control.closest('form');
        const autoSubmit = control.dataset.quantityAutoSubmit === 'true';
        let timer = null;

        const clamp = value => {
            const minimum = Number(input.min || 1);
            const maximum = Number(input.max || 99);
            return Math.min(maximum, Math.max(minimum, value));
        };

        const persist = () => {
            if (!autoSubmit || !form) return;
            refreshCartTotals();
            if (timer) clearTimeout(timer);
            timer = setTimeout(() => form.submit(), 400);
        };

        const updateValue = value => {
            const next = clamp(value);
            if (String(next) === input.value) return;
            input.value = String(next);
            input.dispatchEvent(new Event('change', { bubbles: true }));
            persist();
        };

        decrease?.addEventListener('click', () => updateValue(Number(input.value || input.min || 1) - 1));
        increase?.addEventListener('click', () => updateValue(Number(input.value || input.min || 1) + 1));
        input.addEventListener('change', persist);
    });

    refreshCartTotals();
}

function initializeRevealAnimations() {
    const elements = document.querySelectorAll('.reveal');
    if (!elements.length) return;

    if (!('IntersectionObserver' in window) || window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        elements.forEach(element => element.classList.add('is-visible'));
        return;
    }

    const observer = new IntersectionObserver(entries => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.classList.add('is-visible');
                observer.unobserve(entry.target);
            }
        });
    }, { threshold: 0.12, rootMargin: '0px 0px -40px' });

    elements.forEach(element => observer.observe(element));
}

function initializeAssistant() {
    const form = document.getElementById('assistantForm');
    const input = document.getElementById('assistantMessage');
    const messages = document.getElementById('assistantMessages');
    if (!form || !input || !messages) return;

    const appendMessage = (text, isUser) => {
        const bubble = document.createElement('div');
        bubble.className = `assistant-bubble${isUser ? ' is-user' : ''}`;
        bubble.textContent = text;
        messages.appendChild(bubble);
        messages.scrollTop = messages.scrollHeight;
    };

    const thinking = document.createElement('div');
    thinking.className = 'assistant-bubble assistant-bubble-pending';
    thinking.innerHTML = '<span class="assistant-typing" aria-hidden="true"><i></i><i></i><i></i></span><span class="visually-hidden">Thinking</span>';

    const stripMarkers = value => value
        .replace(/\*\*(.+?)\*\*/g, '$1')
        .replace(/(^|[\s(])\*(?!\s)(.+?)\*/g, '$1$2')
        .replace(/^#+\s*/, '')
        .replace(/`/g, '');

    const renderAnswer = (text, fromSmartModel) => {
        const bubble = document.createElement('div');
        bubble.className = 'assistant-bubble';

        if (fromSmartModel) {
            const tag = document.createElement('span');
            tag.className = 'assistant-source';
            tag.textContent = 'Live assistant';
            bubble.appendChild(tag);
        }

        let list = null;

        text.split(/\r?\n/).forEach(line => {
            const trimmed = line.trim();

            if (trimmed.length === 0) {
                list = null;
                return;
            }

            const bullet = trimmed.match(/^[-*]\s+(.*)$/) || trimmed.match(/^\d+[.)]\s+(.*)$/);

            if (bullet) {
                if (!list) {
                    list = document.createElement('ul');
                    list.className = 'assistant-list';
                    bubble.appendChild(list);
                }

                const item = document.createElement('li');
                item.textContent = stripMarkers(bullet[1]);
                list.appendChild(item);
                return;
            }

            list = null;

            const paragraph = document.createElement('p');
            paragraph.textContent = stripMarkers(trimmed);
            bubble.appendChild(paragraph);
        });

        messages.appendChild(bubble);
        messages.scrollTop = messages.scrollHeight;
    };

    const sendMessage = async text => {
        const cleanText = text.trim();
        if (!cleanText || form.dataset.busy === 'true') return;

        appendMessage(cleanText, true);
        input.value = '';
        form.dataset.busy = 'true';
        messages.appendChild(thinking);
        messages.scrollTop = messages.scrollHeight;

        const tokenField = form.querySelector('input[name="__RequestVerificationToken"]');

        try {
            const body = new URLSearchParams({ question: cleanText });
            if (tokenField) body.append('__RequestVerificationToken', tokenField.value);

            const response = await fetch(form.action || '/Assistant/Ask', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'X-Requested-With': 'XMLHttpRequest' },
                body: body.toString(),
                credentials: 'same-origin'
            });

            if (!response.ok) throw new Error(`Request failed with status ${response.status}`);

            const payload = await response.json();
            thinking.remove();

            const answer = typeof payload?.message === 'string' ? payload.message.trim() : '';

            if (!answer) {
                appendMessage('I could not find an answer to that. Try asking about market days, produce or directions.');
                return;
            }

            renderAnswer(answer, payload.mode === 'smart');
        } catch (error) {
            thinking.remove();
            appendMessage('The assistant is temporarily unavailable. Please browse the catalogue while we reconnect.');
        } finally {
            form.dataset.busy = 'false';
            input.focus();
        }
    };

    form.addEventListener('submit', event => {
        event.preventDefault();
        sendMessage(input.value);
    });

    document.querySelectorAll('[data-assistant-prompt]').forEach(button => {
        button.addEventListener('click', () => sendMessage(button.getAttribute('data-assistant-prompt') || ''));
    });
}

let toastTimer;
function showToast(message) {
    const toastElement = document.getElementById('demoToast');
    const toastMessage = document.getElementById('demoToastMessage');
    if (!toastElement || !toastMessage || !window.bootstrap) return;

    toastMessage.textContent = message;
    const toast = bootstrap.Toast.getOrCreateInstance(toastElement, { delay: 2600 });
    toast.show();

    window.clearTimeout(toastTimer);
    toastTimer = window.setTimeout(() => toast.hide(), 2700);
}
