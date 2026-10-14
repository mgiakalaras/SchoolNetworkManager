// Tooltips για τα γραφήματα του dashboard
(function () {
    function toSvgPoint(svg, event) {
        const point = svg.createSVGPoint();
        point.x = event.clientX;
        point.y = event.clientY;
        const matrix = svg.getScreenCTM();
        return matrix ? point.matrixTransform(matrix.inverse()) : point;
    }

    function placeTooltip(wrap, tooltip, event) {
        const box = wrap.getBoundingClientRect();
        let left = event.clientX - box.left + 14;
        let top = event.clientY - box.top - 10;

        tooltip.hidden = false;

        const width = tooltip.offsetWidth;
        if (left + width > box.width) left = event.clientX - box.left - width - 14;
        if (left < 0) left = 0;
        if (top < 0) top = 0;

        tooltip.style.left = left + "px";
        tooltip.style.top = top + "px";
    }

    function escapeHtml(text) {
        const div = document.createElement("div");
        div.textContent = text == null ? "" : String(text);
        return div.innerHTML;
    }

    // ---------- Μπάρες ----------
    document.querySelectorAll('.chart-wrap[data-chart="bars"]').forEach(function (wrap) {
        const tooltip = wrap.querySelector(".chart-tooltip");

        wrap.querySelectorAll(".bar-group").forEach(function (group) {
            group.addEventListener("pointermove", function (event) {
                const lines = (group.dataset.tip || "").split("|");
                tooltip.innerHTML = lines
                    .map((line, i) => i === 0 ? "<strong>" + escapeHtml(line) + "</strong>" : escapeHtml(line))
                    .join("<br />");
                wrap.querySelectorAll(".bar-group.is-hover").forEach(g => g.classList.remove("is-hover"));
                group.classList.add("is-hover");
                placeTooltip(wrap, tooltip, event);
            });

            group.addEventListener("pointerleave", function () {
                group.classList.remove("is-hover");
                tooltip.hidden = true;
            });
        });
    });

    // ---------- Γραμμές ταχύτητας ----------
    document.querySelectorAll('.chart-wrap[data-chart="lines"]').forEach(function (wrap) {
        const svg = wrap.querySelector("svg");
        const tooltip = wrap.querySelector(".chart-tooltip");
        const dataEl = wrap.querySelector(".chart-data");
        const hit = wrap.querySelector(".hit-area");
        const crosshair = wrap.querySelector(".crosshair");
        const dotDown = wrap.querySelector(".hover-dot.series-down");
        const dotUp = wrap.querySelector(".hover-dot.series-up");

        let points = [];
        try { points = JSON.parse(dataEl.textContent || "[]"); } catch (e) { }
        if (!points.length || !hit) return;

        function hide() {
            tooltip.hidden = true;
            crosshair.style.display = "none";
            dotDown.style.display = "none";
            dotUp.style.display = "none";
        }

        function show(event) {
            const p = toSvgPoint(svg, event);
            let nearest = points[0];
            let best = Infinity;

            for (const point of points) {
                const distance = Math.abs(point.x - p.x);
                if (distance < best) { best = distance; nearest = point; }
            }

            crosshair.setAttribute("x1", nearest.x);
            crosshair.setAttribute("x2", nearest.x);
            crosshair.style.display = "";

            if (nearest.yd != null) {
                dotDown.setAttribute("cx", nearest.x);
                dotDown.setAttribute("cy", nearest.yd);
                dotDown.style.display = "";
            } else {
                dotDown.style.display = "none";
            }

            if (nearest.yu != null) {
                dotUp.setAttribute("cx", nearest.x);
                dotUp.setAttribute("cy", nearest.yu);
                dotUp.style.display = "";
            } else {
                dotUp.style.display = "none";
            }

            const rows = ["<strong>" + escapeHtml(nearest.when) + "</strong>"];
            if (nearest.d != null) rows.push('<span class="legend-swatch series-down"></span>Λήψη: ' + escapeHtml(nearest.d) + " Mbps");
            if (nearest.u != null) rows.push('<span class="legend-swatch series-up"></span>Αποστολή: ' + escapeHtml(nearest.u) + " Mbps");
            if (nearest.p != null) rows.push("Ping: " + escapeHtml(nearest.p) + " ms");

            tooltip.innerHTML = rows.join("<br />");
            placeTooltip(wrap, tooltip, event);
        }

        hit.addEventListener("pointermove", show);
        hit.addEventListener("pointerdown", show);
        hit.addEventListener("pointerleave", hide);
    });
})();
