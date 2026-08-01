using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SchemaRag.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    country = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    stock_quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                    table.CheckConstraint("ck_products_stock_quantity", "stock_quantity >= 0");
                    table.CheckConstraint("ck_products_unit_price", "unit_price >= 0");
                    table.ForeignKey(
                        name: "FK_products_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    order_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.id);
                    table.CheckConstraint("ck_orders_total_amount", "total_amount >= 0");
                    table.ForeignKey(
                        name: "FK_orders_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_items", x => x.id);
                    table.CheckConstraint("ck_order_items_quantity", "quantity > 0");
                    table.CheckConstraint("ck_order_items_unit_price", "unit_price >= 0");
                    table.ForeignKey(
                        name: "FK_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_order_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "categories",
                columns: new[] { "id", "description", "name" },
                values: new object[,]
                {
                    { 1, "Desk and office accessories.", "Office" },
                    { 2, "Computer and audio electronics.", "Electronics" },
                    { 3, "Paper goods and writing supplies.", "Stationery" }
                });

            migrationBuilder.InsertData(
                table: "customers",
                columns: new[] { "id", "country", "created_at", "email", "name" },
                values: new object[,]
                {
                    { 1, "United Kingdom", new DateTime(2024, 1, 10, 12, 0, 0, 0, DateTimeKind.Utc), "ada@example.test", "Ada Lovelace" },
                    { 2, "United States", new DateTime(2024, 2, 5, 12, 0, 0, 0, DateTimeKind.Utc), "grace@example.test", "Grace Hopper" },
                    { 3, "Netherlands", new DateTime(2024, 3, 12, 12, 0, 0, 0, DateTimeKind.Utc), "edsger@example.test", "Edsger Dijkstra" },
                    { 4, "United States", new DateTime(2024, 4, 18, 12, 0, 0, 0, DateTimeKind.Utc), "margaret@example.test", "Margaret Hamilton" },
                    { 5, "United Kingdom", new DateTime(2024, 5, 9, 12, 0, 0, 0, DateTimeKind.Utc), "alan@example.test", "Alan Turing" }
                });

            migrationBuilder.InsertData(
                table: "orders",
                columns: new[] { "id", "customer_id", "order_date", "status", "total_amount" },
                values: new object[,]
                {
                    { 1, 1, new DateOnly(2025, 1, 10), "Completed", 138.90m },
                    { 2, 2, new DateOnly(2025, 1, 22), "Completed", 79.00m },
                    { 3, 1, new DateOnly(2025, 2, 15), "Completed", 199.00m },
                    { 4, 3, new DateOnly(2025, 2, 21), "Completed", 118.00m },
                    { 5, 4, new DateOnly(2025, 3, 3), "Completed", 79.00m },
                    { 6, 5, new DateOnly(2025, 3, 18), "Completed", 90.00m },
                    { 7, 2, new DateOnly(2025, 4, 5), "Completed", 78.50m },
                    { 8, 3, new DateOnly(2025, 4, 27), "Completed", 178.00m },
                    { 9, 4, new DateOnly(2025, 5, 11), "Completed", 99.80m },
                    { 10, 1, new DateOnly(2025, 5, 23), "Completed", 118.50m },
                    { 11, 5, new DateOnly(2025, 6, 2), "Completed", 258.00m },
                    { 12, 2, new DateOnly(2025, 6, 19), "Completed", 135.00m }
                });

            migrationBuilder.InsertData(
                table: "products",
                columns: new[] { "id", "category_id", "name", "sku", "stock_quantity", "unit_price" },
                values: new object[,]
                {
                    { 1, 1, "Laptop Stand", "OFF-STAND-01", 24, 49.90m },
                    { 2, 2, "Mechanical Keyboard", "ELEC-KEY-01", 12, 89.00m },
                    { 3, 2, "Wireless Mouse", "ELEC-MOUSE-01", 18, 39.50m },
                    { 4, 2, "Noise-Cancelling Headphones", "ELEC-AUDIO-01", 4, 199.00m },
                    { 5, 2, "Webcam", "ELEC-CAM-01", 6, 79.00m },
                    { 6, 3, "Hardcover Notebook", "STAT-NOTE-01", 40, 6.50m },
                    { 7, 1, "Desk Lamp", "OFF-LAMP-01", 9, 45.00m },
                    { 8, 2, "USB-C Hub", "ELEC-HUB-01", 7, 59.00m }
                });

            migrationBuilder.InsertData(
                table: "order_items",
                columns: new[] { "id", "order_id", "product_id", "quantity", "unit_price" },
                values: new object[,]
                {
                    { 1, 1, 1, 1, 49.90m },
                    { 2, 1, 2, 1, 89.00m },
                    { 3, 2, 3, 2, 39.50m },
                    { 4, 3, 4, 1, 199.00m },
                    { 5, 4, 8, 2, 59.00m },
                    { 6, 5, 5, 1, 79.00m },
                    { 7, 6, 7, 2, 45.00m },
                    { 8, 7, 8, 1, 59.00m },
                    { 9, 7, 6, 3, 6.50m },
                    { 10, 8, 2, 2, 89.00m },
                    { 11, 9, 1, 2, 49.90m },
                    { 12, 10, 3, 3, 39.50m },
                    { 13, 11, 4, 1, 199.00m },
                    { 14, 11, 8, 1, 59.00m },
                    { 15, 12, 7, 3, 45.00m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_categories_name",
                table: "categories",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customers_email",
                table: "customers",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_items_order_id",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_product_id",
                table: "order_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_orders_customer_id",
                table: "orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_orders_order_date",
                table: "orders",
                column: "order_date");

            migrationBuilder.CreateIndex(
                name: "IX_products_category_id",
                table: "products",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_products_sku",
                table: "products",
                column: "sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_items");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "categories");
        }
    }
}