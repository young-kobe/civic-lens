interface FooterProps {
    timestamp: string;
}

/**
 * Site footer. Contact, legal disclaimer, last refresh.
 * Sits above the mobile bottom-tab bar via CSS margin.
 */
function Footer({ timestamp }: FooterProps) {
    return (
        <footer className="site-footer" role="contentinfo">
            <div className="site-footer-grid">
                <div>
                    <div className="site-footer-heading">Civic Lens</div>
                    <p>
                        Sampled political media analysis. Every metric on this site traces
                        back to the raw text it came from, with a confidence score attached.
                        This is a sample of what we collected, not a poll of the public.
                    </p>
                </div>
                <div>
                    <div className="site-footer-heading">Contact</div>
                    <ul className="site-footer-list">
                        <li>
                            <a href="mailto:kobe@pourengineering.com">
                                kobe@pourengineering.com
                            </a>
                        </li>
                        <li>Built by Kobe Young</li>
                    </ul>
                </div>
                <div>
                    <div className="site-footer-heading">Disclaimer</div>
                    <p>
                        Civic Lens is an independent research project. It does not measure
                        national sentiment, public opinion, or causal propagation. AI
                        classifications are probabilistic and can be wrong. Nothing here is
                        legal, financial, medical, or political advice.
                    </p>
                </div>
            </div>
            <div className="site-footer-bottom">
                <span>Civic Lens &middot; Open Source Media Analytics</span>
                <span>Refreshed {timestamp}</span>
            </div>
        </footer>
    );
}

export default Footer;
