"""Read pinned legislative XML and keep quoted statutes in their enclosing sections."""
import hashlib
from xml.etree import ElementTree


def read_xml(directory, name, digest):
    content = (directory / f"{name}.xml").read_bytes()
    if hashlib.sha256(content).hexdigest() != digest:
        raise ValueError(f"Unexpected source digest for {name}")
    return ElementTree.fromstring(content)


def sections(element):
    # A quoted amendment to existing law belongs to its enclosing bill section.
    if element.tag == "section":
        yield element
    elif element.tag != "quoted-block":
        for child in element:
            yield from sections(child)
