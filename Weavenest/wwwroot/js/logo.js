window.weavenestLogo = (() => {
    const instances = {};

    function createInstance(canvas) {
        const ctx = canvas.getContext('2d');
        const W = 480, H = 480;
        const cx = W / 2, cy = H / 2;

        const SPOKE_COUNT = 6;
        const innerPulses = Array.from({ length: SPOKE_COUNT }, (_, i) => ({ progress: i / SPOKE_COUNT }));
        const outerPulses = Array.from({ length: SPOKE_COUNT }, (_, i) => ({ progress: i / SPOKE_COUNT }));
        const ripples = [{ progress: 0 }, { progress: 0.33 }, { progress: 0.66 }];

        let prevTime = null;
        let t = 0;
        let rafId = null;

        function hexPts(hcx, hcy, r, offsetAngle = 0) {
            return Array.from({ length: 6 }, (_, i) => {
                const a = (Math.PI / 3) * i + offsetAngle;
                return [hcx + r * Math.cos(a), hcy + r * Math.sin(a)];
            });
        }

        function draw(timestamp) {
            if (!prevTime) prevTime = timestamp;
            const dt = Math.min((timestamp - prevTime) / 1000, 0.05);
            prevTime = timestamp;
            t += dt;

            ctx.clearRect(0, 0, W, H);

            const innerR = 88;
            const outerR = 150;
            const spokeAngle = -Math.PI / 2;
            const innerVerts = hexPts(cx, cy, innerR, spokeAngle);
            const outerVerts = hexPts(cx, cy, outerR, spokeAngle);

            // ripple rings
            ripples.forEach(r => {
                r.progress += dt / 6;
                if (r.progress > 1) r.progress -= 1;
                const rr = r.progress * outerR * 1.3;
                const alpha = Math.sin(r.progress * Math.PI) * 0.35;
                ctx.beginPath();
                ctx.arc(cx, cy, rr, 0, Math.PI * 2);
                ctx.strokeStyle = `rgba(127,119,221,${alpha})`;
                ctx.lineWidth = 1.2;
                ctx.stroke();
            });

            // rotating outer hex
            const hexRotA = hexPts(cx, cy, outerR, spokeAngle + t * (Math.PI * 2 / 90));
            ctx.beginPath();
            hexRotA.forEach(([x, y], i) => i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y));
            ctx.closePath();
            ctx.strokeStyle = 'rgba(127,119,221,0.18)';
            ctx.lineWidth = 0.8;
            ctx.stroke();

            // counter-rotating mid hex
            const hexRotB = hexPts(cx, cy, outerR * 0.72, spokeAngle - t * (Math.PI * 2 / 120));
            ctx.beginPath();
            hexRotB.forEach(([x, y], i) => i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y));
            ctx.closePath();
            ctx.strokeStyle = 'rgba(175,169,236,0.12)';
            ctx.lineWidth = 0.6;
            ctx.stroke();

            // cross-web diagonal filaments
            ctx.strokeStyle = 'rgba(175,169,236,0.07)';
            ctx.lineWidth = 0.5;
            for (let i = 0; i < 6; i++) {
                const [x1, y1] = outerVerts[i];
                const [x2, y2] = outerVerts[(i + 2) % 6];
                ctx.beginPath(); ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.stroke();
                const [x3, y3] = outerVerts[(i + 3) % 6];
                ctx.beginPath(); ctx.moveTo(x1, y1); ctx.lineTo(x3, y3); ctx.stroke();
            }

            // inner hex facets
            innerVerts.forEach(([x, y], i) => {
                const [nx, ny] = innerVerts[(i + 1) % 6];
                const phase = Math.sin(t * (Math.PI * 2 / 8) - i * (Math.PI / 3));
                const alpha = 0.04 + phase * 0.12;
                ctx.beginPath();
                ctx.moveTo(cx, cy);
                ctx.lineTo(x, y);
                ctx.lineTo(nx, ny);
                ctx.closePath();
                ctx.fillStyle = i % 2 === 0
                    ? `rgba(175,169,236,${Math.max(0, alpha)})`
                    : `rgba(83,74,183,${Math.max(0, alpha)})`;
                ctx.fill();
            });

            // inner hex border
            ctx.beginPath();
            innerVerts.forEach(([x, y], i) => i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y));
            ctx.closePath();
            ctx.strokeStyle = 'rgba(127,119,221,0.45)';
            ctx.lineWidth = 1;
            ctx.stroke();

            // outer spoke threads + pulses
            outerPulses.forEach((p, i) => {
                p.progress += dt / 5;
                if (p.progress > 1) p.progress -= 1;
                const [x1, y1] = innerVerts[i];
                const [x2, y2] = outerVerts[i];
                ctx.beginPath();
                ctx.moveTo(x1, y1);
                ctx.lineTo(x2, y2);
                ctx.strokeStyle = 'rgba(127,119,221,0.25)';
                ctx.lineWidth = 0.7;
                ctx.stroke();
                const px = x1 + (x2 - x1) * p.progress;
                const py = y1 + (y2 - y1) * p.progress;
                const da = Math.sin(p.progress * Math.PI) * 0.9;
                ctx.beginPath();
                ctx.arc(px, py, 2, 0, Math.PI * 2);
                ctx.fillStyle = `rgba(175,169,236,${da})`;
                ctx.fill();
            });

            // inner spoke lines + pulses
            innerPulses.forEach((p, i) => {
                p.progress += dt / 3;
                if (p.progress > 1) p.progress -= 1;
                const [x2, y2] = innerVerts[i];
                ctx.beginPath();
                ctx.moveTo(cx, cy);
                ctx.lineTo(x2, y2);
                ctx.strokeStyle = 'rgba(175,169,236,0.35)';
                ctx.lineWidth = 0.7;
                ctx.stroke();
                const px = cx + (x2 - cx) * p.progress;
                const py = cy + (y2 - cy) * p.progress;
                const da = Math.sin(p.progress * Math.PI) * 0.95;
                ctx.beginPath();
                ctx.arc(px, py, 1.8, 0, Math.PI * 2);
                ctx.fillStyle = `rgba(238,237,254,${da})`;
                ctx.fill();
            });

            // satellite node halos
            outerVerts.forEach(([x, y], i) => {
                const phase = Math.sin(t * (Math.PI * 2 / 5) - i * (Math.PI / 3));
                const haloR = 7 + phase * 5;
                const alpha = 0.12 + phase * 0.22;
                ctx.beginPath();
                ctx.arc(x, y, haloR, 0, Math.PI * 2);
                ctx.strokeStyle = `rgba(127,119,221,${alpha})`;
                ctx.lineWidth = 0.8;
                ctx.stroke();
            });

            // satellite nodes
            outerVerts.forEach(([x, y], i) => {
                const phase = Math.sin(t * (Math.PI * 2 / 5) - i * (Math.PI / 3));
                const r = 4 + phase * 1.5;
                const grad = ctx.createRadialGradient(x, y, 0, x, y, r * 2);
                grad.addColorStop(0, 'rgba(175,169,236,0.95)');
                grad.addColorStop(1, 'rgba(83,74,183,0.6)');
                ctx.beginPath();
                ctx.arc(x, y, r, 0, Math.PI * 2);
                ctx.fillStyle = grad;
                ctx.fill();
            });

            // inner hex vertex nodes
            innerVerts.forEach(([x, y], i) => {
                const phase = Math.sin(t * (Math.PI * 2 / 6) - i * (Math.PI / 3));
                const r = 2.2 + phase * 0.8;
                ctx.beginPath();
                ctx.arc(x, y, r, 0, Math.PI * 2);
                ctx.fillStyle = `rgba(175,169,236,${0.5 + phase * 0.4})`;
                ctx.fill();
            });

            // core halo layers
            const coreBreath = Math.sin(t * (Math.PI * 2 / 4));
            [[20, 0.08], [14, 0.14], [10, 0.2]].forEach(([r, a]) => {
                ctx.beginPath();
                ctx.arc(cx, cy, r + coreBreath * 3, 0, Math.PI * 2);
                ctx.fillStyle = `rgba(83,74,183,${a})`;
                ctx.fill();
            });

            // core node
            const coreR = 9 + coreBreath * 1.5;
            const coreGrad = ctx.createRadialGradient(cx, cy, 0, cx, cy, coreR);
            coreGrad.addColorStop(0, '#EEEDFE');
            coreGrad.addColorStop(0.55, '#AFA9EC');
            coreGrad.addColorStop(1, 'rgba(83,74,183,0.85)');
            ctx.beginPath();
            ctx.arc(cx, cy, coreR, 0, Math.PI * 2);
            ctx.fillStyle = coreGrad;
            ctx.fill();

            // core inner dot
            const dotAlpha = 0.6 + Math.sin(t * (Math.PI * 2 / 3)) * 0.35;
            ctx.beginPath();
            ctx.arc(cx, cy, 3.5, 0, Math.PI * 2);
            ctx.fillStyle = `rgba(238,237,254,${dotAlpha})`;
            ctx.fill();

            rafId = requestAnimationFrame(draw);
        }

        rafId = requestAnimationFrame(draw);
        return { cancel: () => { if (rafId) { cancelAnimationFrame(rafId); rafId = null; } } };
    }

    function start(canvasId) {
        if (instances[canvasId]) return;
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        instances[canvasId] = createInstance(canvas);
    }

    function stop(canvasId) {
        if (instances[canvasId]) {
            instances[canvasId].cancel();
            delete instances[canvasId];
        }
    }

    return { start, stop };
})();
