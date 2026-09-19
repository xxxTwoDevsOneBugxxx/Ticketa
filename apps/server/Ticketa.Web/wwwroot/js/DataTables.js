export function initDataTable(url, columns, options = {}) {
    const ajaxData = options.ajaxData;
    delete options.ajaxData;

    const customAjax = options.ajax;
    delete options.ajax;

    const applyDaisyPagination = (container) => {
        if (!container) {
            return;
        }

        const paging = container.querySelector('.dt-paging');
        const nav = container.querySelector('.dt-paging nav');

        if (!paging || !nav) {
            return;
        }

        paging.classList.add('flex', 'justify-center', 'my-2');
    };

    const defaultAjax = {
        url: url,
        type: 'GET',
        data: (d) => {
            for (const key of Object.keys(d)) {
                if (key.startsWith('columns[')) delete d[key];
            }
            if (typeof (ajaxData) === "function") {
                Object.assign(d, ajaxData());
            }
        },
        error: (xhr) => {
            if (xhr.status === 401 || xhr.status === 403) {
                window.location.href = '/Auth/Login';
                return;
            }
            console.error('DataTable AJAX error:', xhr.status, xhr.responseText?.substring(0, 200));
        }
    };

    const initialize = () => {
        new DataTable('#DataTable', {
            ajax: customAjax || defaultAjax,
            columns,
            dom: "<'flex flex-col md:flex-row md:items-center md:justify-between gap-3 mb-4'<''l><'segmentedFilter'><'flex items-center gap-2'f>>t<i'mt-5 flex flex-col items-center gap-3'<'text-sm opacity-70'p>>",
            drawCallback: function () {
                applyDaisyPagination(this.api().table().container());
            },
            infoCallback: function () {
                const pageInfo = this.api().page.info();
                return `Page ${pageInfo.page + 1} of ${pageInfo.pages}`;
            },
            initComplete: function () {
                applyDaisyPagination(this.api().table().container());
            },
            stateSave: true,
            ...options
        });
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize, { once: true });
    } else {
        initialize();
    }
}