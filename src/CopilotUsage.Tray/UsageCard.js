function readUsageCard(root) {
    const clean = value => (value || "").replace(/\s+/g, " ").trim();
    const visible = element => {
        const style = getComputedStyle(element);
        return style.display !== "none" && style.visibility !== "hidden" &&
            element.getClientRects().length > 0;
    };
    const account = root.querySelector('meta[name="user-login"]')?.content;
    if (!account) return { status: "signed-out" };

    const headings = [...root.querySelectorAll("h1,h2,h3,h4,h5,h6,span,strong,div,p")]
        .filter(element => clean(element.textContent) === "Usage this cycle" && visible(element));
    const cards = new Set();
    for (const heading of headings) {
        // Find the smallest enclosing card, never collect the whole settings page.
        let node = heading;
        while (node && node !== root.body && node !== root.documentElement) {
            const text = clean(node.innerText);
            if (text.length > 2000) break;
            if (/AI credits/i.test(text)) {
                cards.add(node);
                break;
            }
            node = node.parentElement;
        }
    }
    if (cards.size === 0) return { status: "loading" };
    if (cards.size !== 1) return { status: "ambiguous" };
    const card = [...cards][0];
    // The number and reset caption may be siblings inside the bordered card.
    let probe = card;
    while (!/\bResets?\b/i.test(probe.innerText)) {
        const parent = probe.parentElement;
        if (parent && parent !== root.body && parent !== root.documentElement && parent.tagName !== "MAIN" &&
            clean(parent.innerText).length <= 2000 &&
            (parent.innerText.match(/Usage this cycle/g) || []).length === 1 &&
            (parent.innerText.match(/AI credits/gi) || []).length === 1) {
            probe = parent;
        } else break;
    }
    const container = /\bResets?\b/i.test(probe.innerText) ? probe : card;
    const usageText = clean(container.innerText);
    const resetElements = [...container.querySelectorAll("*")]
        .filter(element => visible(element) && /^Resets?\b/i.test(clean(element.innerText)))
        .sort((a, b) => clean(a.innerText).length - clean(b.innerText).length);
    const resetElement = resetElements.find(element => /\b\d{4}\b/.test(element.innerText)) || resetElements[0];
    const resetText = resetElement ? clean(resetElement.innerText) : "";
    return { status: "ready", account, usageText, resetText };
}
