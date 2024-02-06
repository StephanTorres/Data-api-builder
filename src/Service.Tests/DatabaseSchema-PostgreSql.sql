-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

DROP VIEW IF EXISTS books_view_all;
DROP VIEW IF EXISTS books_view_with_mapping;
DROP VIEW IF EXISTS stocks_view_selected;
DROP VIEW IF EXISTS books_publishers_view_composite;
DROP VIEW IF EXISTS books_publishers_view_composite_insertable;
DROP TABLE IF EXISTS book_author_link;
DROP TABLE IF EXISTS reviews;
DROP TABLE IF EXISTS authors;
DROP TABLE IF EXISTS book_website_placements;
DROP TABLE IF EXISTS website_users;
DROP TABLE IF EXISTS books;
DROP TABLE IF EXISTS players;
DROP TABLE IF EXISTS clubs;
DROP TABLE IF EXISTS publishers;
DROP TABLE IF EXISTS foo.magazines;
DROP TABLE IF EXISTS stocks_price;
DROP TABLE IF EXISTS stocks;
DROP TABLE IF EXISTS comics;
DROP TABLE IF EXISTS brokers;
DROP TABLE IF EXISTS type_table;
DROP TABLE IF EXISTS trees;
DROP TABLE IF EXISTS fungi;
DROP TABLE IF EXISTS empty_table;
DROP TABLE IF EXISTS aow;
DROP TABLE IF EXISTS notebooks;
DROP TABLE IF EXISTS journals;
DROP TABLE IF EXISTS series;
DROP TABLE IF EXISTS sales;
DROP TABLE IF EXISTS graphql_incompatible;
DROP TABLE IF EXISTS GQLmappings;
DROP TABLE IF EXISTS bookmarks;
DROP TABLE IF EXISTS mappedbookmarks;
DROP TABLE IF EXISTS books_sold;
DROP TABLE IF EXISTS default_with_function_table;
DROP FUNCTION IF EXISTS insertCompositeView;

DROP SCHEMA IF EXISTS foo;

CREATE SCHEMA foo;

CREATE TABLE publishers(
    id int GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    name text NOT NULL
);

CREATE TABLE books(
    id int GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    title text NOT NULL,
    publisher_id int NOT NULL
);

CREATE TABLE clubs(
    id int GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    name text NOT NULL
);

CREATE TABLE players(
    id int GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    name text NOT NULL,
    current_club_id int NOT NULL,
    new_club_id int NOT NULL
);

CREATE TABLE book_website_placements(
    id int GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    book_id int UNIQUE NOT NULL,
    price int NOT NULL
);

CREATE TABLE website_users(
    id int PRIMARY KEY,
    username text NULL
);

CREATE TABLE authors(
    id int GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    name text NOT NULL,
    birthdate text NOT NULL
);

CREATE TABLE reviews(
    book_id int,
    id int GENERATED BY DEFAULT AS IDENTITY,
    content text DEFAULT 'Its a classic' NOT NULL,
    PRIMARY KEY(book_id, id)
);

CREATE TABLE book_author_link(
    book_id int NOT NULL,
    author_id int NOT NULL,
    PRIMARY KEY(book_id, author_id)
);

CREATE TABLE foo.magazines(
    id int PRIMARY KEY,
    title text NOT NULL,
    issue_number int NULL
);

CREATE TABLE comics(
    id int PRIMARY KEY,
    title text NOT NULL,
    volume int GENERATED BY DEFAULT AS IDENTITY,
    "categoryName" varchar(100) NOT NULL UNIQUE,
    series_id int NULL
);

CREATE TABLE stocks(
    categoryid int NOT NULL,
    pieceid int NOT NULL,
    "categoryName" varchar(100) NOT NULL,
    "piecesAvailable" int DEFAULT 0,
    "piecesRequired" int DEFAULT 0 NOT NULL,
    PRIMARY KEY(categoryid, pieceid)
);

CREATE TABLE stocks_price(
    categoryid int NOT NULL,
    pieceid int NOT NULL,
    instant timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
    price float,
    is_wholesale_price boolean,
    PRIMARY KEY(categoryid, pieceid, instant)
);

CREATE TABLE brokers(
    "ID Number" int PRIMARY KEY,
    "First Name" text NOT NULL,
    "Last Name" text NOT NULL
);

CREATE TABLE type_table(
    id int GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    -- pg has no byte type
    short_types smallint,
    int_types int,
    long_types bigint,
    string_types text,
    single_types real,
    float_types float,
    decimal_types decimal(38, 19),
    boolean_types boolean,
    datetime_types timestamp,
    bytearray_types bytea,
    uuid_types uuid DEFAULT gen_random_uuid ()
);

CREATE TABLE trees (
    "treeId" int PRIMARY KEY,
    species text,
    region text,
    height text
);

CREATE TABLE fungi (
    speciesid int PRIMARY KEY,
    region text
);

CREATE TABLE empty_table (
    id int PRIMARY KEY
);

CREATE TABLE notebooks (
    id int PRIMARY KEY,
    notebookname text,
    color text,
    ownername text
);

CREATE TABLE journals (
    id int PRIMARY KEY,
    journalname text,
    color text,
    ownername text
);

CREATE TABLE aow (
    "NoteNum" int PRIMARY KEY,
    "DetailAssessmentAndPlanning" text,
    "WagingWar" text,
    "StrategicAttack" text
);

CREATE TABLE series (
    id int PRIMARY KEY,
    name text NOT NULL
);

CREATE TABLE sales (
    id int GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    item_name text NOT NULL,
    subtotal float NOT NULL,
    tax float NOT NULL,
    total float generated always as (subtotal + tax) stored
);

CREATE TABLE graphql_incompatible (
    __typeName int PRIMARY KEY,
    conformingName text
);

CREATE TABLE GQLmappings (
    __column1 int PRIMARY KEY,
    __column2 text,
    column3 text
);

CREATE TABLE bookmarks(
    id int GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    bkname text NOT NULL
);

CREATE TABLE mappedbookmarks(
    id int GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    bkname text NOT NULL
);

CREATE TABLE books_sold (
  id int PRIMARY KEY,
  book_name TEXT,
  copies_sold INT DEFAULT 0,
  last_sold_on TIMESTAMP DEFAULT '9999-12-31 23:59:59.997',
  last_sold_on_date TIMESTAMP GENERATED ALWAYS AS (last_sold_on) STORED
);

CREATE TABLE default_with_function_table
(
    id int GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    user_value INT,
    "current_date" TIMESTAMP DEFAULT CURRENT_DATE NOT NULL,
    "current_timestamp" TIMESTAMP DEFAULT NOW() NOT NULL,
    random_number INT DEFAULT FLOOR(RANDOM() * 1000),
    next_date TIMESTAMP DEFAULT (CURRENT_DATE + INTERVAL '1 day'),
    default_string_with_parenthesis VARCHAR(100) DEFAULT '()',
    default_function_string_with_parenthesis VARCHAR(100) DEFAULT 'NOW()',
    default_integer INT DEFAULT 100,
    default_date_string TIMESTAMP DEFAULT '1999-01-08 10:23:54'
);

ALTER TABLE books
ADD CONSTRAINT book_publisher_fk
FOREIGN KEY (publisher_id)
REFERENCES publishers (id)
ON DELETE CASCADE;

ALTER TABLE players
ADD CONSTRAINT player_club_fk
FOREIGN KEY (current_club_id)
REFERENCES clubs (id)
ON DELETE CASCADE;

ALTER TABLE book_website_placements
ADD CONSTRAINT book_website_placement_book_fk
FOREIGN KEY (book_id)
REFERENCES books (id)
ON DELETE CASCADE;

ALTER TABLE reviews
ADD CONSTRAINT review_book_fk
FOREIGN KEY (book_id)
REFERENCES books (id)
ON DELETE CASCADE;

ALTER TABLE book_author_link
ADD CONSTRAINT book_author_link_book_fk
FOREIGN KEY (book_id)
REFERENCES books (id)
ON DELETE CASCADE;

ALTER TABLE book_author_link
ADD CONSTRAINT book_author_link_author_fk
FOREIGN KEY (author_id)
REFERENCES authors (id)
ON DELETE CASCADE;

ALTER TABLE stocks
ADD CONSTRAINT stocks_comics_fk
FOREIGN KEY ("categoryName")
REFERENCES comics ("categoryName")
ON DELETE CASCADE;

ALTER TABLE stocks_price
ADD CONSTRAINT stocks_price_stocks_fk
FOREIGN KEY (categoryid, pieceid)
REFERENCES stocks (categoryid, pieceid)
ON DELETE CASCADE;

ALTER TABLE comics
ADD CONSTRAINT comics_series_fk
FOREIGN KEY (series_id)
REFERENCES series(id)
ON DELETE CASCADE;

INSERT INTO bookmarks (id, bkname)
SELECT
	value,
    CONCAT('Test Item #' , value)
FROM 
    GENERATE_SERIES(1, 10000, 1) as value;

INSERT INTO mappedbookmarks (id, bkname)
SELECT
	value,
    CONCAT('Test Item #' , value)
FROM 
    GENERATE_SERIES(1, 10000, 1) as value;

INSERT INTO GQLmappings(__column1, __column2, column3) VALUES (1, 'Incompatible GraphQL Name', 'Compatible GraphQL Name');
INSERT INTO GQLmappings(__column1, __column2, column3) VALUES (3, 'Old Value', 'Record to be Updated');
INSERT INTO GQLmappings(__column1, __column2, column3) VALUES (4, 'Lost Record', 'Record to be Deleted');
INSERT INTO GQLmappings(__column1, __column2, column3) VALUES (5, 'Filtered Record', 'Record to be Filtered on Find');
INSERT INTO publishers(id, name) VALUES (1234, 'Big Company'), (2345, 'Small Town Publisher'), (2323, 'TBD Publishing One'), (2324, 'TBD Publishing Two Ltd'), (1940, 'Policy Publisher 01'), (1941, 'Policy Publisher 02'), (1156, 'The First Publisher');
INSERT INTO clubs(id, name) VALUES (1111, 'Manchester United'), (1112, 'FC Barcelona'), (1113, 'Real Madrid');
INSERT INTO players(id, name, current_club_id, new_club_id)
    VALUES 
        (1, 'Cristiano Ronaldo', 1113, 1111),
        (2, 'Leonel Messi', 1112, 1113);
INSERT INTO authors(id, name, birthdate) VALUES (123, 'Jelte', '2001-01-01'), (124, 'Aniruddh', '2002-02-02'), (125, 'Aniruddh', '2001-01-01'), (126, 'Aaron', '2001-01-01');
INSERT INTO books(id, title, publisher_id)
    VALUES
        (1, 'Awesome book', 1234),
        (2, 'Also Awesome book', 1234),
        (3, 'Great wall of china explained', 2345),
        (4, 'US history in a nutshell', 2345),
        (5, 'Chernobyl Diaries', 2323),
        (6, 'The Palace Door', 2324),
        (7, 'The Groovy Bar', 2324),
        (8, 'Time to Eat', 2324),
        (9, 'Policy-Test-01', 1940),
        (10, 'Policy-Test-02', 1940),
        (11, 'Policy-Test-04', 1941),
        (12, 'Time to Eat 2', 1941),
        (13, 'Before Sunrise', 1234),
        (14, 'Before Sunset', 1234);
INSERT INTO book_website_placements(book_id, price) VALUES (1, 100), (2, 50), (3, 23), (5, 33);
INSERT INTO website_users(id, username) VALUES (1, 'George'), (2, NULL), (3, ''), (4, 'book_lover_95'), (5, 'null');
INSERT INTO book_author_link(book_id, author_id) VALUES (1, 123), (2, 124), (3, 123), (3, 124), (4, 123), (4, 124), (5, 126);;
INSERT INTO reviews(id, book_id, content) VALUES (567, 1, 'Indeed a great book'), (568, 1, 'I loved it'), (569, 1, 'best book I read in years');
INSERT INTO foo.magazines(id, title, issue_number) VALUES (1, 'Vogue', 1234), (11, 'Sports Illustrated', NULL), (3, 'Fitness', NULL);
INSERT INTO series(id, name) VALUES (3001, 'Foundation'), (3002, 'Hyperion Cantos');
INSERT INTO comics(id, title, "categoryName", series_id)
VALUES (1, 'Star Trek', 'SciFi', NULL), (2, 'Cinderella', 'Tales', 3001),(3,'Únknown','', 3002), (4, 'Alexander the Great', 'Historical', NULL);INSERT INTO stocks(categoryid, pieceid, "categoryName") VALUES (1, 1, 'SciFi'), (2, 1, 'Tales'),(0,1,''),(100, 99, 'Historical');
INSERT INTO brokers("ID Number", "First Name", "Last Name") VALUES (1, 'Michael', 'Burry'), (2, 'Jordan', 'Belfort');
INSERT INTO stocks_price(categoryid, pieceid, price, is_wholesale_price) VALUES (2, 1, 100.57, True), (1, 1, 42.75, False);
INSERT INTO stocks_price(categoryid, pieceid, instant, price, is_wholesale_price) VALUES (2, 1, '2023-08-21 15:11:04', 100.57, True);
INSERT INTO type_table(id, short_types, int_types, long_types, string_types, single_types, float_types, decimal_types, boolean_types, datetime_types, bytearray_types) VALUES
    (1, 1, 1, 1, '', 0.33, 0.33, 0.333333, true, '1999-01-08 10:23:54', '\xABCDEF0123'),
    (2, -1, -1, -1, 'lksa;jdflasdf;alsdflksdfkldj', -9.2, -9.2, -9.292929, false, '19990108 10:23:00', '\x98AB7511AABB1234'),
    (3, -32768, -2147483648, -9223372036854775808, '', -3.4E38, -1.7E308, 2.929292E-19, true, '1753-01-01 00:00:00.000', '\x00000000'),
    (4, 32767, 2147483647, 9223372036854775807, 'null', 3.4E38, 1.7E308, 2.929292E-14, true, '9999-12-31 23:59:59.997', '\xFFFFFFFF'),
    (5, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL);
INSERT INTO type_table(id, uuid_types) values(10, 'D1D021A8-47B4-4AE4-B718-98E89C41A161');
INSERT INTO trees("treeId", species, region, height) VALUES (1, 'Tsuga terophylla', 'Pacific Northwest', '30m'), (2, 'Pseudotsuga menziesii', 'Pacific Northwest', '40m');
INSERT INTO fungi(speciesid, region) VALUES (1, 'northeast'), (2, 'southwest');
INSERT INTO notebooks(id, noteBookName, color, ownerName) VALUES (1, 'Notebook1', 'red', 'Sean'), (2, 'Notebook2', 'green', 'Ani'), (3, 'Notebook3', 'blue', 'Jarupat'), (4, 'Notebook4', 'yellow', 'Aaron');
INSERT INTO journals(id, journalname, color, ownername) VALUES (1, 'Journal1', 'red', 'Sean'), (2, 'Journal2', 'green', 'Ani'), (3, 'Journal3', 'blue', 'Jarupat'), (4, 'Journal4', 'yellow', 'Aaron');

INSERT INTO aow("NoteNum", "DetailAssessmentAndPlanning", "WagingWar", "StrategicAttack") VALUES (1, 'chapter one notes: ', 'chapter two notes: ', 'chapter three notes: ');
INSERT INTO sales(id, item_name, subtotal, tax) VALUES (1, 'Watch', 249.00, 20.59), (2, 'Montior', 120.50, 11.12);
INSERT INTO books_sold(id,book_name,last_sold_on) values(1, 'Awesome Book', CURRENT_DATE);
--Starting with id > 5000 is chosen arbitrarily so that the incremented id-s won't conflict with the manually inserted ids in this script
--Sequence counter is set to 5000 so the next autogenerated id will be 5001

SELECT setval('books_id_seq', 5000);
SELECT setval('book_website_placements_id_seq', 5000);
SELECT setval('publishers_id_seq', 5000);
SELECT setval('authors_id_seq', 5000);
SELECT setval('reviews_id_seq', 5000);
SELECT setval('type_table_id_seq', 5000);
SELECT setval('sales_id_seq', 5000);
SELECT setval('players_id_seq', 5000);
SELECT setval('clubs_id_seq', 5000);
SELECT setval('default_with_function_table_id_seq', 5000);

CREATE VIEW books_view_all AS SELECT * FROM books;
CREATE VIEW books_view_with_mapping AS SELECT * FROM books;
CREATE VIEW stocks_view_selected AS
    SELECT categoryid, pieceid, "categoryName", "piecesAvailable"
    FROM stocks;
CREATE VIEW books_publishers_view_composite as SELECT
    publishers.name, books.id, books.title, publishers.id as pub_id
    FROM books, publishers
    where publishers.id = books.publisher_id;
CREATE VIEW books_publishers_view_composite_insertable as SELECT
    books.id, books.title, publishers.name, books.publisher_id
    FROM books, publishers
    WHERE publishers.id = books.publisher_id;

CREATE FUNCTION insertCompositeView() RETURNS trigger AS $$
BEGIN
    INSERT INTO books(title, publisher_id) VALUES (new.title, new.publisher_id) RETURNING id INTO new.id;
    SELECT name INTO new.name FROM publishers WHERE id = new.publisher_id;
    RETURN new;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER insertCompositeViewTrigger INSTEAD OF INSERT ON books_publishers_view_composite_insertable
  FOR EACH ROW EXECUTE PROCEDURE insertCompositeView();
