DROP TABLE IF EXISTS book_author_link;
DROP TABLE IF EXISTS reviews;
DROP TABLE IF EXISTS authors;
DROP TABLE IF EXISTS books;
DROP TABLE IF EXISTS publishers;
CREATE TABLE publishers(
    id bigint IDENTITY(5000, 1) PRIMARY KEY,
    name text NOT NULL
);

CREATE TABLE books(
    id bigint IDENTITY(5000, 1) PRIMARY KEY,
    title text NOT NULL,
    publisher_id bigint NOT NULL
);

CREATE TABLE authors(
    id bigint IDENTITY(5000, 1) PRIMARY KEY,
    name text NOT NULL,
    birthdate text NOT NULL
);

CREATE TABLE reviews(
    book_id bigint,
    id bigint IDENTITY(5000, 1),
    content text,
    PRIMARY KEY(book_id, id)
);

CREATE TABLE book_author_link(
    book_id bigint NOT NULL,
    author_id bigint NOT NULL,
    PRIMARY KEY(book_id, author_id)
);

ALTER TABLE books
ADD CONSTRAINT book_publisher_fk
FOREIGN KEY (publisher_id)
REFERENCES publishers (id);

ALTER TABLE reviews
ADD CONSTRAINT review_book_fk
FOREIGN KEY (book_id)
REFERENCES books (id);

ALTER TABLE book_author_link
ADD CONSTRAINT book_author_link_book_fk
FOREIGN KEY (book_id)
REFERENCES books (id);

ALTER TABLE book_author_link
ADD CONSTRAINT book_author_link_author_fk
FOREIGN KEY (author_id)
REFERENCES authors (id);

SET IDENTITY_INSERT publishers ON
INSERT INTO publishers(id, name) VALUES (1234, 'Big Company'), (2345, 'Small Town Publisher');
SET IDENTITY_INSERT publishers OFF

SET IDENTITY_INSERT authors ON
INSERT INTO authors(id, name, birthdate) VALUES (123, 'Jelte', '2001-01-01'), (124, 'Aniruddh', '2002-02-02');
SET IDENTITY_INSERT authors OFF

SET IDENTITY_INSERT books ON
INSERT INTO books(id, title, publisher_id) VALUES (1, 'Awesome book', 1234), (2, 'Also Awesome book', 1234), (3, 'Great wall of china explained', 2345), (4, 'US history in a nutshell', 2345);
SET IDENTITY_INSERT books OFF

INSERT INTO book_author_link(book_id, author_id) VALUES (1, 123), (2, 124), (3, 123), (3, 124), (4, 123), (4, 124);

SET IDENTITY_INSERT reviews ON
INSERT INTO reviews(id, book_id, content) VALUES (567, 1, 'Indeed a great book'), (568, 1, 'I loved it'), (569, 1, 'best book I read in years');
SET IDENTITY_INSERT reviews OFF


