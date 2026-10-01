(function () {
    "use strict";

    var aliasToggle = document.getElementById("edit-namespace-alias");
    var aliasForm = document.getElementById("namespace-alias-form");
    if (aliasToggle && aliasForm) {
        function closeAliasEditor() {
            aliasForm.reset();
            aliasForm.hidden = true;
            aliasToggle.setAttribute("aria-expanded", "false");
            aliasToggle.focus();
        }
        aliasToggle.addEventListener("click", function () {
            if (!aliasForm.hidden) { closeAliasEditor(); return; }
            aliasForm.hidden = false;
            aliasToggle.setAttribute("aria-expanded", "true");
            document.getElementById("namespace-alias").focus();
        });
        document.getElementById("cancel-namespace-alias").addEventListener("click", closeAliasEditor);
        aliasForm.addEventListener("keydown", function (event) {
            if (event.key === "Escape") { event.preventDefault(); closeAliasEditor(); }
        });
    }

    var navToggle = document.querySelector(".mobile-nav-toggle");
    if (navToggle) {
        var masthead = document.querySelector(".masthead");
        var mobileNavigation = window.matchMedia("(max-width: 1100px)");
        function setNavigationOpen(open) {
            navToggle.setAttribute("aria-expanded", String(open));
            navToggle.setAttribute("aria-label", open ? "Close navigation" : "Open navigation");
        }
        navToggle.hidden = false;
        navToggle.addEventListener("click", function () {
            setNavigationOpen(navToggle.getAttribute("aria-expanded") !== "true");
        });
        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape" && mobileNavigation.matches && navToggle.getAttribute("aria-expanded") === "true") {
                setNavigationOpen(false);
                navToggle.focus();
            }
        });
        document.addEventListener("click", function (event) {
            if (!masthead.contains(event.target)) { setNavigationOpen(false); }
        });
        mobileNavigation.addEventListener("change", function () { setNavigationOpen(false); });
    }

    // Fetch server-rendered dashboard content without navigating or replacing the layout.
    var dashboard = document.getElementById("dashboard-content");
    if (dashboard && window.jQuery) {
        var $ = window.jQuery;
        function setRefreshing(refreshing) {
            var indicator = dashboard.querySelector(".refresh-progress");
            if (indicator) { indicator.hidden = !refreshing; }
            dashboard.setAttribute("aria-busy", String(refreshing));
        }
        function scheduleRefresh() {
            var seconds = parseInt(dashboard.getAttribute("data-refresh-seconds"), 10);
            if (!(seconds > 0)) { return; }
            window.setTimeout(refreshDashboard, seconds * 1000);
        }
        function refreshDashboard() {
            // Keep keyboard focus and any interaction in the dashboard undisturbed.
            if (document.hidden || dashboard.contains(document.activeElement)) {
                scheduleRefresh();
                return;
            }
            setRefreshing(true);
            $.ajax({
                url: dashboard.getAttribute("data-refresh-url"),
                dataType: "html",
                cache: false,
                timeout: 30000
            }).done(function (html) {
                var updated = $( $.parseHTML(html) ).filter("#dashboard-content");
                // Login redirects and unexpected responses must not replace the dashboard.
                if (updated.length !== 1 || dashboard.contains(document.activeElement)) { return; }
                var scrollX = window.scrollX;
                var scrollY = window.scrollY;
                dashboard.innerHTML = updated[0].innerHTML;
                dashboard.setAttribute("data-refresh-seconds", updated.attr("data-refresh-seconds"));
                window.scrollTo(scrollX, scrollY);
            }).always(function () {
                setRefreshing(false);
                scheduleRefresh();
            });
        }
        scheduleRefresh();
    }

    // Client-side table filter for the namespace and node lists.
    document.querySelectorAll("[data-filter-target]").forEach(function (input) {
        var selector = input.getAttribute("data-filter-target");
        input.addEventListener("input", function () {
            var needle = input.value.trim().toLowerCase();
            document.querySelectorAll(selector + " tbody tr").forEach(function (row) {
                row.hidden = needle.length > 0 && row.textContent.toLowerCase().indexOf(needle) === -1;
            });
        });
    });

    // Copy-to-clipboard for the join command.
    document.querySelectorAll("[data-copy-target]").forEach(function (button) {
        button.addEventListener("click", function () {
            var source = document.querySelector(button.getAttribute("data-copy-target"));
            if (!source || !navigator.clipboard) { return; }
            navigator.clipboard.writeText(source.textContent.trim()).then(function () {
                var original = button.textContent;
                button.textContent = "Copied";
                window.setTimeout(function () { button.textContent = original; }, 1600);
            });
        });
    });

    // Destructive forms require the exact object name to be typed before they submit.
    document.querySelectorAll("form[data-confirm-name]").forEach(function (form) {
        var expected = form.getAttribute("data-confirm-name");
        var input = form.querySelector("input[name='confirmName']");
        var submit = form.querySelector("button[type='submit']");
        if (!input || !submit) { return; }
        submit.disabled = true;
        input.addEventListener("input", function () {
            submit.disabled = input.value !== expected;
        });
    });
})();
