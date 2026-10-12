import { initDataTable } from "../DataTables.js";

const imageBase = "https://image.tmdb.org/t/p/w200";
const trailerKeyCache = new Map();
const pageCache = new Map();

function toDataTableDate(value) {
    if (!value) return "";
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return "";

    const options = {
        year: 'numeric', month: 'short', day: 'numeric',
        hour: '2-digit', minute: '2-digit'
    };
    return date.toLocaleDateString(undefined, options);
}

function renderMovieSkeletons(count = 5) {
    let html = '';
    for (let i = 0; i < count; i++) {
        html += `
            <tr class="animate-pulse">
                <td class="p-0 border-0 bg-transparent align-top">
                    <div class="border border-base-300 bg-base-100 rounded-xl overflow-hidden shadow-sm mb-4">
                        <div class="flex items-center gap-4 p-4">
                            <div class="w-12 h-16 bg-base-300 rounded shrink-0"></div>
                            <div class="flex-1 space-y-2.5 min-w-0">
                                <div class="h-5 w-48 bg-base-300 rounded"></div>
                                <div class="h-3.5 w-28 bg-base-300 rounded"></div>
                            </div>
                            <div class="h-7 w-28 rounded-full bg-base-300 shrink-0"></div>
                            <div class="w-5 h-5 rounded bg-base-300 shrink-0"></div>
                        </div>
                    </div>
                </td>
            </tr>
        `;
    }
    return html;
}

function schedulePrefetch(currentStart, length, totalRecords, filter, searchVal) {
    const nextStart = currentStart + length;
    if (nextStart < totalRecords) {
        const nextKey = `${filter}_${searchVal}_${nextStart}_${length}`;
        if (!pageCache.has(nextKey)) {
            const params = new URLSearchParams({
                draw: '0',
                start: nextStart.toString(),
                length: length.toString(),
                'search[value]': searchVal,
                segmentedFilter: filter
            });

            fetch(`/Showtime/GetAll?${params.toString()}`)
                .then(res => res.ok ? res.json() : null)
                .then(data => {
                    if (data && data.data) {
                        pageCache.set(nextKey, data);
                    }
                })
                .catch(() => {});
        }
    }
}

function getTrailerModalElements() {
    return {
        trailerModal: document.getElementById("trailer-modal"),
        trailerModalPanel: document.getElementById("trailer-modal-panel"),
        trailerModalTitle: document.getElementById("trailer-modal-title"),
        trailerModalIframe: document.getElementById("trailer-modal-iframe"),
        trailerModalCloseButton: document.getElementById("trailer-modal-close")
    };
}

function getModalTargetRect() {
    const viewportPadding = 20;
    const maxWidth = Math.min(window.innerWidth - (viewportPadding * 2), 1080);
    const maxHeight = Math.min(window.innerHeight - (viewportPadding * 2), 620);

    return {
        left: Math.max((window.innerWidth - maxWidth) / 2, viewportPadding),
        top: Math.max((window.innerHeight - maxHeight) / 2, viewportPadding),
        width: maxWidth,
        height: maxHeight,
        borderRadius: 16
    };
}

function setModalPanelRect(panel, rect) {
    panel.style.left = `${rect.left}px`;
    panel.style.top = `${rect.top}px`;
    panel.style.width = `${rect.width}px`;
    panel.style.height = `${rect.height}px`;
    panel.style.borderRadius = `${rect.borderRadius}px`;
}

function buildTrailerEmbedUrl(videoId) {
    return `https://www.youtube.com/embed/${encodeURIComponent(videoId)}?autoplay=1`;
}

async function resolveTrailerKey(initialKey, tmdbId) {
    if (initialKey) return initialKey;
    if (!tmdbId) return "";
    if (trailerKeyCache.has(tmdbId)) return trailerKeyCache.get(tmdbId);

    const response = await fetch(`/Movies/GetTrailerKey?tmdbId=${encodeURIComponent(tmdbId)}`);
    if (!response.ok) return "";

    const payload = await response.json();
    const key = payload?.key ?? "";
    trailerKeyCache.set(tmdbId, key);
    return key;
}

const trailerModalRuntime = {
    triggerRect: null,
    closeTimer: null,
    requestToken: 0,
    initialized: false
};

function closeMovieTrailerModal() {
    const { trailerModal, trailerModalPanel, trailerModalIframe } = getTrailerModalElements();
    if (!trailerModal || !trailerModalPanel || !trailerModalIframe || trailerModal.hidden) return;

    trailerModal.classList.remove("is-open");
    trailerModalPanel.classList.remove("is-expanded");
    setModalPanelRect(trailerModalPanel, trailerModalRuntime.triggerRect || {
        left: (window.innerWidth / 2) - 28,
        top: (window.innerHeight / 2) - 28,
        width: 56,
        height: 56,
        borderRadius: 18
    });

    trailerModalRuntime.closeTimer = window.setTimeout(() => {
        trailerModal.hidden = true;
        trailerModal.setAttribute("aria-hidden", "true");
        trailerModalIframe.src = "";
        document.body.classList.remove("trailer-modal-lock");
    }, 460);
}

function ensureTrailerModalHandlers() {
    if (trailerModalRuntime.initialized) return;

    const { trailerModal, trailerModalCloseButton, trailerModalPanel } = getTrailerModalElements();
    if (!trailerModal || !trailerModalCloseButton || !trailerModalPanel) return;

    trailerModal.addEventListener("click", event => {
        if (event.target.matches("[data-modal-close]")) closeMovieTrailerModal();
    });

    trailerModalCloseButton.addEventListener("click", closeMovieTrailerModal);

    document.addEventListener("keydown", event => {
        if (event.key === "Escape") closeMovieTrailerModal();
    });

    window.addEventListener("resize", () => {
        if (!trailerModal.hidden) setModalPanelRect(trailerModalPanel, getModalTargetRect());
    });

    trailerModalRuntime.initialized = true;
}

window.openMovieTrailer = async function (triggerButton, title, trailerKey = "", tmdbId = "") {
    const { trailerModal, trailerModalPanel, trailerModalTitle, trailerModalIframe } = getTrailerModalElements();
    if (!trailerModal || !trailerModalPanel || !trailerModalTitle || !trailerModalIframe || !triggerButton) return;

    ensureTrailerModalHandlers();

    const sourceRect = triggerButton.getBoundingClientRect();
    trailerModalRuntime.triggerRect = {
        left: sourceRect.left,
        top: sourceRect.top,
        width: sourceRect.width,
        height: sourceRect.height,
        borderRadius: Math.min(sourceRect.width, sourceRect.height) * 0.34
    };

    window.clearTimeout(trailerModalRuntime.closeTimer);
    trailerModal.hidden = false;
    trailerModal.setAttribute("aria-hidden", "false");
    trailerModalTitle.textContent = `${title} trailer`;
    trailerModalIframe.src = "";
    setModalPanelRect(trailerModalPanel, trailerModalRuntime.triggerRect);
    const requestToken = ++trailerModalRuntime.requestToken;

    requestAnimationFrame(() => {
        trailerModal.classList.add("is-open");
        trailerModalPanel.classList.add("is-expanded");
        setModalPanelRect(trailerModalPanel, getModalTargetRect());
    });

    window.setTimeout(async () => {
        const key = await resolveTrailerKey(trailerKey, tmdbId);
        if (requestToken !== trailerModalRuntime.requestToken || trailerModal.hidden) return;

        if (!key) {
            trailerModalTitle.textContent = `${title} trailer unavailable`;
            return;
        }
        trailerModalIframe.src = buildTrailerEmbedUrl(key);
    }, 220);

    document.body.classList.add("trailer-modal-lock");
};

function initSegmentedFilter(onChange) {
    const segmentedRoot = document.getElementById("mySegmentedFilter");
    if (!segmentedRoot) return;

    const mountSegmentedFilter = () => {
        const host = document.querySelector(".segmentedFilter");
        if (!host) return false;

        if (segmentedRoot.parentElement !== host) {
            host.appendChild(segmentedRoot);
            segmentedRoot.style.display = "";
            segmentedRoot.classList.remove("hidden");
        }
        return true;
    };

    if (!mountSegmentedFilter()) {
        const observer = new MutationObserver(() => {
            if (mountSegmentedFilter()) observer.disconnect();
        });
        observer.observe(document.body, { childList: true, subtree: true });
        window.setTimeout(() => observer.disconnect(), 5000);
    }

    const segmented = segmentedRoot.querySelector(".msf-track");
    if (!segmented) return;

    const pill = segmented.querySelector(".msf-pill");
    const items = Array.from(segmented.querySelectorAll(".msf-btn"));
    if (!pill || items.length === 0) return;

    const movePillTo = (item) => {
        pill.style.width = `${item.offsetWidth}px`;
        pill.style.transform = `translateX(${item.offsetLeft}px)`;
    };

    const getActiveItem = () => segmented.querySelector(".msf-btn.is-active") ?? items[0];

    const setActive = (item) => {
        items.forEach(button => {
            const isCurrent = button === item;
            button.classList.toggle("is-active", isCurrent);
            button.setAttribute("aria-selected", isCurrent ? "true" : "false");
        });
        movePillTo(item);
        if (typeof onChange === 'function') {
            onChange(item.dataset.segment || "all");
        }
    };

    items.forEach(item => {
        item.addEventListener("mouseenter", () => movePillTo(item));
        item.addEventListener("focus", () => movePillTo(item));
        item.addEventListener("click", () => setActive(item));
    });

    segmented.addEventListener("mouseleave", () => movePillTo(getActiveItem()));
    window.addEventListener("resize", () => movePillTo(getActiveItem()));
    movePillTo(getActiveItem());
}

const dataTableElement = document.getElementById("DataTable");

// Initialize TomSelect inside modal safely
const observeModalForTomSelect = () => {
    const modalContent = document.getElementById("modalContent");
    if (!modalContent) return;

    const observer = new MutationObserver(() => {
        const dropdown = document.getElementById("movie-select-dropdown");
        if (dropdown && !dropdown.classList.contains("tomselected") && typeof window.TomSelect !== "undefined") {
            new window.TomSelect("#movie-select-dropdown", {
                render: {
                    option: function (data, escape) {
                        const poster = data.dataset && data.dataset.poster;
                        const imgSrc = poster ? `https://image.tmdb.org/t/p/w92${poster}` : '';
                        return `
                            <div class="flex items-center gap-3 py-1.5">
                                ${imgSrc
                                ? `<img src="${imgSrc}" class="h-10 w-7 shrink-0 rounded object-cover" />`
                                : `<div class="h-10 w-7 shrink-0 rounded bg-base-300"></div>`}
                                <div class="min-w-0">
                                    <div class="truncate text-sm font-medium">${escape(data.text)}</div>
                                </div>
                            </div>`;
                    },
                    item: function (data, escape) {
                        const poster = data.dataset && data.dataset.poster;
                        const imgSrc = poster ? `https://image.tmdb.org/t/p/w92${poster}` : '';
                        return `
                            <div class="flex items-center gap-2">
                                ${imgSrc
                                ? `<img src="${imgSrc}" class="h-6 w-4 shrink-0 rounded object-cover" />`
                                : ``}
                                <div class="truncate font-medium">${escape(data.text)}</div>
                            </div>`;
                    }
                }
            });
        }
    });

    observer.observe(modalContent, { childList: true, subtree: true });
};

observeModalForTomSelect();

let currentUrl = "/Showtime/GetAll";

// Optimistic pagination feedback listener
document.addEventListener("click", (e) => {
    const btn = e.target.closest(".dt-paging button");
    if (!btn || btn.disabled) return;

    const container = btn.closest(".dt-paging");
    if (!container) return;

    // Instantly highlight clicked button
    const allBtns = container.querySelectorAll("button");
    allBtns.forEach(b => {
        b.classList.remove("current", "is-active");
        b.removeAttribute("aria-current");
    });
    btn.classList.add("current", "is-active");
    btn.setAttribute("aria-current", "page");
});

if (dataTableElement) {
    let currentFilter = "all";

    initDataTable(currentUrl, [
        {
            data: null,
            orderable: false,
            searchable: false,
            className: "p-0 border-0 bg-transparent align-top bg-white",
            render: function (data, type, row) {
                if (type === 'display') {
                    const poster = row.posterPath ? `<img src="${imageBase}${row.posterPath}" alt="Poster" class="w-12 h-16 object-cover rounded shadow-sm shrink-0" />` : `<div class="w-12 h-16 bg-base-300 rounded flex items-center justify-center text-[10px] text-base-content/50 shrink-0 border border-base-300">
                        <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="opacity-30"><rect width="18" height="18" x="3" y="3" rx="2"/><circle cx="9" cy="9" r="2"/><path d="m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21"/></svg>
                    </div>`;

                    let showtimesHtml = '';
                    if (row.showtimes && row.showtimes.length > 0) {
                        showtimesHtml = row.showtimes.map(st => {
                            const start = new Date(st.startTime);
                            const startDate = start.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
                            const startTimeStr = start.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });

                            const end = new Date(st.endTime);
                            const endDate = end.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
                            const endTimeStr = end.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });

                            const priceFormatted = `${st.price.toFixed(2)} $`;

                            let statusText = "";
                            if (st.status === 0) statusText = "• Scheduled";
                            else if (st.status === 1) statusText = "• Sold Out";
                            else if (st.status === 2) statusText = "• Completed";

                            let colorClass = "";
                            if (st.status === 0) colorClass = "text-blue-700 bg-blue-100";
                            else if (st.status === 1) colorClass = "text-yellow-700 bg-yellow-100";
                            else if (st.status === 2) colorClass = "text-green-700 bg-green-100";

                            const statusHtml = `
                                <span class="inline-flex items-center px-3 py-1 rounded-full text-sm font-semibold ${colorClass}">
                                    ${statusText}
                                </span>
                            `;

                            const fiveHoursFromNow = new Date(new Date().getTime() + (5 * 60 * 60 * 1000));
                            const canEdit = start > fiveHoursFromNow;
                            const tooltipMsg = canEdit ? "Edit" : "Cannot edit within 5 hours of starting";

                            const editButton = canEdit
                                ? `<button type="button" class="btn btn-square btn-outline btn-sm border-base-300 text-primary hover:bg-primary/10 hover:border-primary/40 tooltip" data-tip="${tooltipMsg}" onclick="openModal('createForm', '/showtime/Upsert/' + ${st.id}, 'showtime')">
                                     <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z"/><path d="m15 5 4 4"/></svg>
                                    </button>`
                                : `<button type="button" class="btn btn-square btn-outline btn-sm border-base-300 text-base-content/30 cursor-not-allowed tooltip" data-tip="${tooltipMsg}" disabled>
                                     <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z"/><path d="m15 5 4 4"/></svg>
                                    </button>`;

                            const deleteButton = st.isArchived
                                ? `<button type="button" class="btn btn-square btn-outline btn-sm border-base-300 text-red-500 hover:bg-red-50 hover:border-red-200 tooltip" data-tip="Delete" onclick="openModal('deleteForm', '/Showtime/DeleteConfirmation/${st.id}', 'showtime')">
                                       <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v3M4 7h16" /></svg>
                                   </button>`
                                : '';

                            const trailerButton = `
                                <button type="button" class="btn btn-square btn-outline btn-sm border-base-300 text-primary hover:bg-primary/10 hover:border-primary/40 tooltip" data-tip="Trailer" onclick="openMovieTrailer(this, '${(row.title ?? "Movie").replace(/'/g, "&#39;")}', '${st.trailerKey ?? ""}', '${row.tmdbId ?? ""}')">
                                    <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                        <polygon points="5 3 19 12 5 21 5 3"/>
                                    </svg>
                                </button>`;

                            const mapButton = `
                                <button type="button" class="btn btn-square btn-outline btn-sm border-base-300 text-blue-500 hover:bg-blue-50 hover:border-base-400 tooltip" data-tip="View Seat Map" onclick="openModal('viewMapForm', '/showtime/ViewSeatMap/${st.id}', 'seat map')">
                                    <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="18" height="18" x="3" y="3" rx="2"/><path d="M3 12h18"/><path d="M12 3v18"/></svg>
                                </button>`;

                            return `
                                <div class="flex items-center flex-wrap md:flex-nowrap gap-6 p-5 border-b border-base-300 last:border-b-0 hover:bg-base-200/30 transition-colors">
                                    <div class="flex flex-col min-w-[120px]">
                                        <span class="text-[10px] font-bold text-base-content/60 uppercase tracking-wider mb-1">Hall</span>
                                        <span class="font-bold text-base">${st.hallName}</span>
                                        <span class="text-xs text-base-content/60 mt-0.5">${st.visibleSeatCount} bookable</span>
                                    </div>
                                    
                                    <div class="flex flex-col min-w-[140px]">
                                        <span class="text-[10px] font-bold text-base-content/60 uppercase tracking-wider mb-1">Start</span>
                                        <span class="font-bold text-base">${startDate}</span>
                                        <span class="text-xs text-base-content/60 mt-0.5">${startTimeStr}</span>
                                    </div>
                                    
                                    <div class="flex flex-col min-w-[140px]">
                                        <span class="text-[10px] font-bold text-base-content/60 uppercase tracking-wider mb-1">End</span>
                                        <span class="font-bold text-base">${endDate}</span>
                                        <span class="text-xs text-base-content/60 mt-0.5">${endTimeStr}</span>
                                    </div>
                                    
                                    <div class="flex flex-col min-w-[80px]">
                                        <span class="text-[10px] font-bold text-base-content/60 uppercase tracking-wider mb-1">Price</span>
                                        <span class="font-bold text-base">${priceFormatted}</span>
                                    </div>
                                    
                                    ${st.archivedAt ? `
                                    <div class="flex flex-col min-w-[130px]">
                                        <span class="text-[10px] font-bold text-base-content/60 uppercase tracking-wider mb-1">Completed At</span>
                                        <span class="text-xs text-base-content/60">${toDataTableDate(st.archivedAt)}</span>
                                    </div>
                                    ` : ''}
                                    
                                    <div class="flex-1 flex justify-center min-w-[150px]">
                                        ${statusHtml}
                                    </div>
                                    
                                    <div class="flex items-center gap-2 justify-end">
                                        ${editButton}
                                        ${trailerButton}
                                        ${mapButton}
                                        ${deleteButton}
                                    </div>
                                </div>
                            `;
                        }).join('');
                    }

                    return `
                    <details class="group border bg-base-100 rounded-xl overflow-hidden [&_summary::-webkit-details-marker]:hidden mb-4 shadow-sm w-full block">
                        <summary class="flex items-center gap-4 p-4 cursor-pointer hover:bg-base-200/50 transition-colors list-none outline-none">
                            ${poster}
                            <div class="flex-1 font-semibold text-xl truncate">${row.title}</div>
                            <div class="badge badge-outline border-base-300 bg-base-200/50 text-sm whitespace-nowrap px-3 py-3">${row.showtimeCount} showtimes</div>
                            <div class="text-base-content/50 transition-transform duration-200 group-open:-rotate-180">
                                <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6"/></svg>
                            </div>
                        </summary>
                        <div class="border-t border-base-300 flex flex-col">
                            ${showtimesHtml}
                        </div>
                    </details>
                    `;
                }

                return row.title;
            }
        }
    ], {
        ordering: false,
        bSort: false,
        ajax: function (data, callback, settings) {
            const searchVal = data.search?.value?.trim() ?? "";
            const cacheKey = `${currentFilter}_${searchVal}_${data.start}_${data.length}`;

            if (pageCache.has(cacheKey)) {
                const cached = pageCache.get(cacheKey);
                cached.draw = data.draw;
                callback(cached);
                schedulePrefetch(data.start, data.length, cached.recordsFiltered, currentFilter, searchVal);
                return;
            }

            // Show 5 skeleton rows in table while loading
            const tbody = document.querySelector("#DataTable tbody");
            if (tbody) {
                tbody.innerHTML = renderMovieSkeletons(5);
            }

            const params = new URLSearchParams({
                draw: data.draw.toString(),
                start: data.start.toString(),
                length: data.length.toString(),
                'search[value]': searchVal,
                segmentedFilter: currentFilter
            });

            fetch(`${currentUrl}?${params.toString()}`)
                .then(res => {
                    if (!res.ok) throw new Error(`HTTP ${res.status}`);
                    return res.json();
                })
                .then(result => {
                    pageCache.set(cacheKey, result);
                    callback(result);
                    schedulePrefetch(data.start, data.length, result.recordsFiltered, currentFilter, searchVal);
                })
                .catch(err => {
                    console.error("Failed to load showtimes:", err);
                    callback({
                        draw: data.draw,
                        recordsTotal: 0,
                        recordsFiltered: 0,
                        data: []
                    });
                });
        },
        initComplete: function () {
            const api = this.api();

            initSegmentedFilter((filter) => {
                currentFilter = filter;
                pageCache.clear();
                api.ajax.reload();
            });
        },
        serverSide: true,
        stateLoadParams: (settings, data) => {
            if (data.columns.length !== 1) {
                localStorage.removeItem(`DataTables_${settings.sTableId}_${window.location.pathname}`);
                return false;
            }
        }
    });
}