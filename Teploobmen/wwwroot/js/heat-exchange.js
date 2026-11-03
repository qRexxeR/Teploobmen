(() => {
    'use strict';

    const source = document.getElementById('calculation-data');
    if (!source) return;
    let points;
    try {
        points = JSON.parse(source.textContent).filter(point =>
            ['y', 'material', 'gas', 'delta'].every(key => Number.isFinite(point[key])));
    } catch {
        return;
    }
    if (points.length < 2) return;

    const NS = 'http://www.w3.org/2000/svg';
    const colors = { material: '#c77345', gas: '#32908b', delta: '#607f78' };
    const number = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 });
    const axisNumber = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 3 });

    function element(tag, attributes = {}, text) {
        const node = document.createElementNS(NS, tag);
        Object.entries(attributes).forEach(([name, value]) => node.setAttribute(name, value));
        if (text !== undefined) node.textContent = text;
        return node;
    }

    function niceBounds(values, desired) {
        let low = Math.min(...values);
        let high = Math.max(...values);
        // Temperature charts retain zero when all values have the same sign.
        if (low >= 0) low = 0;
        if (high <= 0) high = 0;
        if (Math.abs(high - low) < 1e-10) {
            low -= 1;
            high += 1;
        }
        const raw = (high - low) / desired;
        const magnitude = 10 ** Math.floor(Math.log10(raw));
        const ratio = raw / magnitude;
        const step = (ratio <= 1 ? 1 : ratio <= 2 ? 2 : ratio <= 2.5 ? 2.5 : ratio <= 5 ? 5 : 10) * magnitude;
        return { low: Math.floor(low / step) * step, high: Math.ceil(high / step) * step, step };
    }

    class ProfileChart {
        constructor(host) {
            this.host = host;
            this.tooltip = host.querySelector('.chart-tooltip');
            this.isDelta = host.dataset.chart === 'delta';
            this.series = this.isDelta ? ['delta'] : ['material', 'gas'];
            this.activeIndex = null;
            this.frame = null;
            this.lastWidth = 0;
            this.draw();

            if ('ResizeObserver' in window) {
                this.observer = new ResizeObserver(() => {
                    if (Math.abs(host.clientWidth - this.lastWidth) < 1) return;
                    cancelAnimationFrame(this.frame);
                    this.frame = requestAnimationFrame(() => this.draw());
                });
                this.observer.observe(host);
            } else {
                window.addEventListener('resize', () => this.draw());
            }
            host.addEventListener('pointermove', event => this.onPointer(event));
            host.addEventListener('pointerleave', () => this.hidePoint());
            host.addEventListener('blur', () => this.hidePoint());
            host.addEventListener('keydown', event => {
                if (!['ArrowLeft', 'ArrowRight', 'Home', 'End', 'Escape'].includes(event.key)) return;
                event.preventDefault();
                if (event.key === 'Escape') return this.hidePoint();
                let index = this.activeIndex ?? 0;
                if (event.key === 'ArrowLeft') index = Math.max(0, index - 1);
                if (event.key === 'ArrowRight') index = Math.min(points.length - 1, index + 1);
                if (event.key === 'Home') index = 0;
                if (event.key === 'End') index = points.length - 1;
                this.showPoint(index);
            });
        }

        draw() {
            const width = this.host.clientWidth;
            if (!width) return;
            this.lastWidth = width;
            const height = this.host.clientHeight;
            const pad = { left: width < 400 ? 43 : 49, right: 16, top: 31, bottom: 44 };
            const plotWidth = width - pad.left - pad.right;
            const plotHeight = height - pad.top - pad.bottom;
            const xmin = points[0].y;
            const xmax = points[points.length - 1].y;
            const bounds = niceBounds(points.flatMap(point => this.series.map(key => point[key])), 4);
            const x = value => pad.left + (value - xmin) / (xmax - xmin) * plotWidth;
            const y = value => pad.top + (bounds.high - value) / (bounds.high - bounds.low) * plotHeight;
            this.geometry = { width, height, pad, plotWidth, plotHeight, x, y, xmin, xmax };

            const previous = this.host.querySelector('svg');
            if (previous) previous.remove();
            const svg = element('svg', { viewBox: `0 0 ${width} ${height}`, 'aria-hidden': 'true', focusable: 'false' });
            const tickStyle = { fill: '#9ba9aa', 'font-size': '10', 'font-family': 'Segoe UI, Arial, sans-serif' };
            const grid = element('g');
            svg.append(grid);
            const steps = Math.min(12, Math.round((bounds.high - bounds.low) / bounds.step));
            for (let index = 0; index <= steps; index++) {
                const value = bounds.low + index * bounds.step;
                const py = y(value);
                grid.append(element('line', { x1: pad.left, x2: width - pad.right, y1: py, y2: py, stroke: '#e8eeea', 'stroke-width': '.8' }));
                grid.append(element('text', { ...tickStyle, x: pad.left - 10, y: py + 3.5, 'text-anchor': 'end' }, axisNumber.format(Math.abs(value) < bounds.step / 100 ? 0 : value)));
            }
            const xsteps = Math.max(2, Math.min(6, Math.floor(plotWidth / 70)));
            for (let index = 0; index <= xsteps; index++) {
                const value = xmin + (xmax - xmin) * index / xsteps;
                const px = x(value);
                grid.append(element('line', { x1: px, x2: px, y1: pad.top, y2: height - pad.bottom, stroke: '#f0f3ef', 'stroke-width': '.8' }));
                grid.append(element('text', { ...tickStyle, x: px, y: height - pad.bottom + 19, 'text-anchor': 'middle' }, axisNumber.format(value)));
            }
            svg.append(element('line', { x1: pad.left, x2: width - pad.right, y1: height - pad.bottom, y2: height - pad.bottom, stroke: '#d5dfda', 'stroke-width': '1' }));
            svg.append(element('text', { ...tickStyle, x: pad.left, y: 15, fill: '#8fa19d', 'font-size': '9' }, this.isDelta ? 'Разность T − t, °C' : 'Температура, °C'));
            svg.append(element('text', { ...tickStyle, x: pad.left + plotWidth / 2, y: height - 4, fill: '#96a7a1', 'font-size': '9', 'text-anchor': 'middle' }, 'Координата y от верха слоя, м'));

            if (this.isDelta) {
                const area = points.map((point, index) => `${index ? 'L' : 'M'}${x(point.y).toFixed(2)},${y(point.delta).toFixed(2)}`).join(' ');
                const baseline = Math.max(bounds.low, Math.min(bounds.high, 0));
                svg.append(element('path', { d: `${area} L${x(xmax)},${y(baseline)} L${x(xmin)},${y(baseline)} Z`, fill: '#6c9b8810', stroke: 'none' }));
            }
            this.series.forEach(key => {
                const d = points.map((point, index) => `${index ? 'L' : 'M'}${x(point.y).toFixed(2)},${y(point[key]).toFixed(2)}`).join(' ');
                const path = element('path', { d, fill: 'none', stroke: colors[key], 'stroke-width': this.isDelta ? '2' : '2.3', 'stroke-linecap': 'round', 'stroke-linejoin': 'round', 'vector-effect': 'non-scaling-stroke' });
                if (key === 'gas') path.setAttribute('stroke-dasharray', '6 4');
                svg.append(path);
            });

            this.hover = element('g', { visibility: 'hidden', 'pointer-events': 'none' });
            this.crosshair = element('line', { y1: pad.top, y2: height - pad.bottom, stroke: '#bdcbc3', 'stroke-dasharray': '3 4', 'stroke-width': '1' });
            this.hover.append(this.crosshair);
            this.markers = this.series.map(key => {
                const circle = element('circle', { r: 3.8, fill: '#fff', stroke: colors[key], 'stroke-width': 2 });
                this.hover.append(circle);
                return circle;
            });
            svg.append(this.hover);
            this.host.prepend(svg);
            this.svg = svg;
            if (this.activeIndex !== null) this.showPoint(this.activeIndex);
        }

        onPointer(event) {
            if (!this.geometry) return;
            const rect = this.host.getBoundingClientRect();
            const px = Math.max(this.geometry.pad.left, Math.min(rect.width - this.geometry.pad.right, event.clientX - rect.left));
            const target = this.geometry.xmin + (px - this.geometry.pad.left) / this.geometry.plotWidth * (this.geometry.xmax - this.geometry.xmin);
            let lo = 0, hi = points.length - 1;
            while (lo < hi) {
                const mid = Math.floor((lo + hi) / 2);
                if (points[mid].y < target) lo = mid + 1; else hi = mid;
            }
            if (lo > 0 && target - points[lo - 1].y < points[lo].y - target) lo--;
            this.showPoint(lo);
        }

        showPoint(index) {
            if (!this.geometry || !this.hover) return;
            this.activeIndex = index;
            const point = points[index];
            const px = this.geometry.x(point.y);
            this.crosshair.setAttribute('x1', px);
            this.crosshair.setAttribute('x2', px);
            this.markers.forEach((circle, seriesIndex) => {
                circle.setAttribute('cx', px);
                circle.setAttribute('cy', this.geometry.y(point[this.series[seriesIndex]]));
            });
            this.hover.setAttribute('visibility', 'visible');
            if (!this.tooltip) return;
            const row = (label, value, key) => `<div class="tooltip-row"><span>${key ? `<i class="${key === 'gas' ? 'gas' : 'material'}-dot"></i>` : ''}${label}</span><span>${number.format(value)} °C</span></div>`;
            this.tooltip.innerHTML = `<strong>Высота y = ${number.format(point.y)} м</strong>` + (this.isDelta ? row('Разность T − t', point.delta) : row('Материал', point.material, 'material') + row('Газ', point.gas, 'gas') + row('Разность', point.delta));
            this.tooltip.hidden = false;
            const tooltipWidth = this.tooltip.offsetWidth;
            const left = px + 12 + tooltipWidth <= this.geometry.width - 2 ? px + 12 : px - tooltipWidth - 12;
            this.tooltip.style.left = `${Math.max(4, Math.min(this.geometry.width - tooltipWidth - 4, left))}px`;
            this.tooltip.style.top = `${this.geometry.pad.top + 5}px`;
        }

        hidePoint() {
            this.activeIndex = null;
            if (this.hover) this.hover.setAttribute('visibility', 'hidden');
            if (this.tooltip) this.tooltip.hidden = true;
        }
    }

    document.querySelectorAll('[data-chart]').forEach(host => new ProfileChart(host));
})();
